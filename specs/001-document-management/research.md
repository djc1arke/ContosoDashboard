# Research: Document Upload and Management

**Feature**: `001-document-management` | **Date**: 2026-10-22

All Technical Context unknowns are resolved below. No `NEEDS CLARIFICATION` items remain.

## R1. Target framework and package versions

- **Decision**: Keep `net10.0` (csproj is the source of truth per the constitution) and the existing EF Core 8.0.0 SqlServer packages. Add no new runtime packages to the web project.
- **Rationale**: Everything needed (`InputFile`, minimal APIs, `Stream`, `System.Security.Cryptography`) ships with the shared framework. Fewer dependencies = simpler (Principle VI) and offline-safe (Principle I).
- **Alternatives**: Upgrading EF Core to 10.x – unrelated churn, out of scope.

## R2. File storage abstraction and layout

- **Decision**: `IFileStorageService` with `UploadAsync`, `DeleteAsync`, `DownloadAsync`, `GetUrlAsync` (names mandated by Principle II). `LocalFileStorageService` is the default and stores files under a configurable root (`FileStorage:RootPath`, default `AppData/uploads` relative to the content root — **outside `wwwroot`**). The *caller* (`DocumentService`) generates the storage path `{yyyy}/{MM}/{Guid:N}{canonicalExt}` where `canonicalExt` comes from the allow-list, never from the user-supplied name. The local implementation rejects any resolved path that is not inside the root (defense in depth against traversal). The user's original filename is stored only as metadata (`OriginalFileName`) and sanitized when emitted in `Content-Disposition`.
- **`GetUrlAsync`**: Local implementation returns `null` ("no direct URL"); all delivery goes through the authorization-checked endpoints, which stream from `DownloadAsync`. A future Azure Blob implementation may return a short-lived SAS URL without callers changing.
- **Rationale**: Satisfies FR-013/014/017 and Principle IV; the stored path/key is opaque so the DB schema is unchanged by a cloud swap (`StoragePath` holds a provider-neutral key).
- **Alternatives**: Storing blobs in SQL (`varbinary(max)`) – bloats LocalDB (10 GB cap), harder to swap. Storing under `wwwroot` – violates FR-013.

## R3. Content-vs-extension validation (FR-002/003/004)

- **Decision**: A pure, stateless `DocumentFileRules` helper (not infrastructure, so no interface) holds the allow-list (`.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`), max size (25 MB = 26,214,400 bytes), and signature (magic-byte) checks read from the first bytes of the stream:
  - PDF `%PDF-`; JPEG `FF D8 FF`; PNG `89 50 4E 47 0D 0A 1A 0A`.
  - Legacy Office (`.doc/.xls/.ppt`) OLE2 `D0 CF 11 E0 A1 B1 1A E1`.
  - OOXML (`.docx/.xlsx/.pptx`) ZIP `50 4B 03 04` **plus** a check that the archive contains the expected root part (`word/`, `xl/`, `ppt/`) via `System.IO.Compression.ZipArchive` in read mode.
  - `.txt`: no signature; reject if the first 8 KB contains NUL bytes or is not valid UTF-8/ASCII/UTF-16 BOM text.
  - Zero-byte files rejected.
  Content type served is derived from the canonical extension, never from the client-supplied `Content-Type`.
- **Rationale**: Catches "exe renamed to .pdf" (edge case) with no external dependency.
- **Alternatives**: Third-party MIME sniffing libraries – new dependency without need.

## R4. Malware screening (FR-005, Constitution "Out of scope" clause)

- **Decision**: `IMalwareScanService.ScanAsync(Stream, string fileName, CancellationToken) → MalwareScanResult { IsClean, ThreatName? }`. Default `StubMalwareScanService` is fully offline: it flags content containing the industry-standard **EICAR test string** (so the rejection path is demonstrable/testable) and otherwise reports clean. It is documented in README Known Limitations as a simulation, not real scanning. Scanning runs **before** any file is written to storage.
- **Rationale**: Meets the constitution's "behind an interface with an offline stub" rule and gives a deterministic test hook.
- **Alternatives**: Windows Defender/AMSI integration – host-dependent, not portable for training; ClamAV – external service.

## R5. Upload mechanics, progress, and size limits in Blazor Server

- **Decision**: Use `InputFile` (`multiple`, `accept` list as a UX hint only) and `IBrowserFile.OpenReadStream(maxAllowedSize: 25MB + 1)`. The component copies the stream in 80 KB chunks into a temp buffer/`MemoryStream` (max 25 MB, acceptable for training scale) while updating a per-file `% complete` bound to a Bootstrap progress bar (FR-006); the validated stream is then handed to `IDocumentService.UploadAsync`. Server-side validation is authoritative; client `accept`/size hints are not trusted. Blazor's SignalR `MaximumReceiveMessageSize` is not raised: `InputFile` streams in small chunks, so no change to hub options is needed for 25 MB.
- **Sequence (Principle VI)**: validate size/extension/signature → malware scan → generate GUID path → `UploadAsync` to storage → save metadata in one `SaveChanges`; if the DB save fails, delete the just-written file (compensating action) so no orphan remains (FR-011). Replace uses the same flow: write new file under a new GUID, update the row, then delete the old file; on failure delete the new file and leave the original intact.
- **Rationale**: Idiomatic Blazor Server; avoids a separate multipart HTTP endpoint (and the antiforgery/CSP concerns it would add).
- **Alternatives**: Minimal-API multipart upload with JS XHR progress – needs custom JS, antiforgery wiring; more moving parts.

## R6. Authorization-checked delivery (download and preview)

- **Decision**: Two minimal-API endpoints, mapped in `Program.cs` with `.RequireAuthorization("Employee")`:
  - `GET /documents/{id:int}/download` → `Content-Disposition: attachment`
  - `GET /documents/{id:int}/preview` → inline, **only** for PDF/JPEG/PNG; others → 404
  Each endpoint reads the user id from the `NameIdentifier` claim and calls `IDocumentService.OpenReadAsync(id, userId, accessKind)`; that service method performs the service-level authorization (layer 3), records the activity (`Download` vs `Preview`), and returns `null` for **both** "not found" and "not authorized", so the endpoint returns an identical `404` (FR-016: no existence disclosure). Responses add `X-Content-Type-Options: nosniff` (already global) and `Cache-Control: private, no-store`.
- **Preview presentation**: Open in a new browser tab (`target="_blank"`, `rel="noopener"`). In-page `<iframe>`/`<embed>` is **not** used because the global `X-Frame-Options: DENY` would block even same-origin framing and the constitution forbids weakening the security headers. Images in modals may use `<img src="/documents/{id}/preview">` (allowed by existing `img-src 'self'`). **No CSP change is required** (nothing to document in Known Limitations beyond the stub scanner).
- **Rationale**: Satisfies FR-013/015/016/025/026 at all three layers (policy on endpoint, `[Authorize]` on pages, service check).
- **Alternatives**: Blazor JS-interop `DotNetStreamReference` download – works but bypasses plain links/preview-in-tab and complicates the "direct link / guessing an identifier" scenario test.

## R7. Role-based access model (FR-010, 015, 022–024, 027–031, 035)

- **Decision**: Centralize rules in `DocumentService` using a single reusable EF predicate for **visibility** and a pure method for **management**, both computed from the requesting user's *current DB record* (role, department) — not from claims alone — so role/department changes take effect immediately (spec: team shares evaluated at access time).
  - **View/Preview/Download** if any: uploader; Administrator; the document's project manager; member of the document's project; direct share to user; team share where `share.TeamName == user.Department`; Team Lead whose department equals the uploader's department (they may manage, therefore view).
  - **Edit metadata / Delete** if any: uploader; Administrator; project manager of the document's project; Team Lead (role == TeamLead) with `Department == uploader.Department`.
  - **Replace file / Share / Unshare**: uploader only (FR-028, FR-035).
  - **Associate with a project (upload/edit)**: user is member or manager of that project; Administrator any (FR-010).
  - **Attach to task** (FR-037): user can view the document, is a member/manager of the task's project (Administrator excepted), and the document's project is null (→ adopts task's project) or equals the task's project; else refused with a clear message.
  - Project removal edge case: owner and Administrators retain access via uploader/Administrator rules; other members lose access automatically because membership is evaluated live.
  - The "Team Lead" test uses `UserRole.TeamLead` exactly (not the policy hierarchy), because the spec grants team-scoped management to Team Leads, not to Project Managers.
- **Lists/search** apply the same predicate in the SQL query (`IQueryable` filter) so unauthorized rows are never materialized and never counted (FR-024, "never reveal existence").
- **Alternatives**: ASP.NET `IAuthorizationHandler` resource-based policies – more idiomatic but requires `HttpContext` user in Blazor; a plain service predicate is simpler, testable, and matches how existing services take `requestingUserId`.

## R8. Tags and search

- **Decision**: Separate `DocumentTag` rows (per the spec's Key Entities) with `Tag` normalized to trimmed lowercase, de-duplicated per document, unique index `(DocumentId, Tag)`, max 30 chars, max 10 tags. Search term matches title, description, any tag, uploader `DisplayName`, and project `Name` using `EF.Functions.Like`/`Contains` (translates to `LIKE`), AND-ed with the visibility predicate. Indexes: `Document(UploadedByUserId)`, `(ProjectId)`, `(UploadDate)`, `(Category)`.
- **Rationale**: ≤500 documents per list (FR-044) is well within LIKE-scan performance on LocalDB; full-text indexing would complicate LocalDB setup.
- **Alternatives**: SQL Server full-text search – optional LocalDB feature, not guaranteed installed.

## R9. Sharing model

- **Decision**: `DocumentShare` rows with exactly one of `SharedWithUserId` or `SharedWithTeam` (department name string; "team" = department per spec Assumptions), enforced by a check constraint and filtered unique indexes to prevent duplicates. Removal deletes the row (activity log preserves history). Because access is evaluated live (R7), removal and team-membership changes are effective immediately (FR-031, US6.6/6.7). Share and unshare are owner-only, and notify new recipients (team share → every current user in the department except the owner).
- **Team picker source**: distinct non-empty `User.Department` values via a new `IUserService.GetDepartmentsAsync()`.

## R10. Notifications

- **Decision**: Reuse `INotificationService`. Append two values to the end of `NotificationType`: `DocumentShared`, `DocumentAdded` (appended so existing persisted integer values are unchanged). "Document added to project" notifies all project members and the project manager except the uploader (FR-034). Notification failures must not fail the upload (log and continue), because the document is already committed.

## R11. Audit log and reports

- **Decision**: `DocumentActivity` is append-only (no update/delete API), stores `DocumentId` as a plain integer **without a foreign key** plus denormalized `DocumentTitle` and `FileType`, so records survive document deletion (FR-042). `ActionType` enum: `Upload, Download, Preview, Delete, Share, ShareRemoved` (plus `Replace`/`EditMetadata` are not required by FR-040 and are **not** logged). Recorded in the **same `SaveChanges`** as the action where possible (upload/delete/share) so an action never exists without its record; Download/Preview records are written before streaming begins.
- **Reports** (`IDocumentReportService`, `Administrator` policy + service-level role check): (1) uploads by file type, (2) top uploaders, (3) access patterns = Download/Preview counts grouped by document, by user, and by day over a selectable date range; owner accesses included (spec clarification). Computed on demand with `GroupBy` in SQL; the uploads reports derive from the `Upload` activity rows so they remain correct after deletions.
- **Rationale**: One source of truth (the activity table) for the three reports.

## R12. Task integration (FR-036/037) and missing task details page

- **Finding**: `Tasks.razor` has a `ViewTaskDetails` TODO and **no task details page exists** (`GetTaskByIdAsync` already exists in `ITaskService`).
- **Decision**: Add a minimal `Pages/TaskDetails.razor` (`/tasks/{taskId:int}`, `[Authorize]`) showing task info plus a `TaskDocuments` component (list, attach existing accessible document, upload new). Wire the TODO to navigate there. Task↔Document link table `TaskDocument` (composite key `TaskId, DocumentId`) with `AttachedByUserId`/`AttachedDate`. Detaching a link does not delete the document. Task access is checked through the existing `ITaskService.GetTaskByIdAsync(taskId, userId)`.
- **Alternatives**: Embedding a modal on `Tasks.razor` – the spec calls for a "task detail page."

## R13. Dashboard integration (FR-038/039)

- **Decision**: Extend `DashboardSummary` with `DocumentCount` (documents uploaded by the user) and add `GetRecentDocumentsAsync(userId, 5)` to `IDocumentService`; `Index.razor` renders a Recent Documents card (empty state when none) and a fifth summary card. `DashboardService` stays unaware of documents beyond the count query via the same `DbContext`.

## R14. Schema bootstrap with `EnsureCreated()`

- **Finding**: `EnsureCreated()` does nothing when the database already exists, so existing LocalDB instances will not get the new tables.
- **Decision**: No migrations (consistent with the current approach/constitution). Quickstart documents dropping the dev database once (`dotnet ef database drop --force`) so it is re-created and re-seeded. No document seed rows are added (files do not exist on a fresh machine); seed users/projects remain unchanged and the existing seed continues to work. README gets a note.
- **Cascade-path caution**: SQL Server rejects multiple cascade paths. `Document → Project` and `Document → UploadedBy User` use `Restrict`/`NoAction`; `DocumentTag`, `DocumentShare`, `TaskDocument` cascade from `Document`; `TaskDocument → TaskItem` cascades (task FK to project is already client-set-null so no second path). Verified during implementation by a fresh `EnsureCreated()` run (a quickstart step).

## R15. Test strategy (Principle V)

- **Decision**: Create `ContosoDashboard.Tests` (xUnit, `net10.0`) referencing the web project, using **EF Core SQLite in-memory** (offline, enforces constraints, closer to relational behavior than the InMemory provider) and a temp-directory `LocalFileStorageService`. Tests cover for every service method: success path + unauthorized path (non-member, non-owner, other-team Team Lead, non-recipient, non-admin reports), validation rejection (size, type, signature mismatch, zero-byte, EICAR), no-orphan on failure (storage failure fake, DB failure), share removal/team membership live evaluation, attach-to-task project rules, and activity retention after delete. Endpoint authorization is covered with `WebApplicationFactory` only for the unauthenticated/404-on-unauthorized cases if the cost is acceptable; otherwise by service tests plus the quickstart manual scenarios.
- **Justification**: A second project is the smallest way to satisfy Principle V (no test project exists yet).

## R16. Pagination and performance targets

- **Decision**: Server-side filtering/sorting/paging (page size 25, count query separate) with projection to a lightweight `DocumentListItem` DTO (no `Include` of large graphs; needed navigation values are projected). Meets ≤2 s for 500 documents (FR-044). Streaming uses `FileStream` with `FileOptions.Asynchronous | SequentialScan`; preview time dominated by local disk (FR-045).
