---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (http-endpoints.md, service-interfaces.md, ui-routes.md), quickstart.md, `.specify/memory/constitution.md` v1.0.0

**Tests**: INCLUDED. The spec does not request TDD, but Constitution Principle V (NON-NEGOTIABLE for new services/authorization) requires automated tests covering the success path and at least the unauthorized-access path for every new service, so test tasks are generated per story. Write each story's tests first and confirm they FAIL before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US8 (maps to spec.md user stories); no label in Setup, Foundational, Polish
- All file paths are relative to the repository root. The web project lives in `ContosoDashboard/` (single Blazor Server project); tests live in `ContosoDashboard.Tests/`.

## Path Conventions

- Web app: `ContosoDashboard/Models/`, `ContosoDashboard/Data/`, `ContosoDashboard/Services/`, `ContosoDashboard/Pages/`, `ContosoDashboard/Shared/`, `ContosoDashboard/Program.cs`
- Tests: `ContosoDashboard.Tests/` (xUnit, SQLite in-memory, temp-dir storage)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test project, repo hygiene, configuration, shared test support

- [X] T001 Create xUnit test project `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj` targeting `net10.0` with packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.EntityFrameworkCore.Sqlite` 8.0.x (tests only) and a `ProjectReference` to `ContosoDashboard/ContosoDashboard.csproj`; no solution file exists, so do not create one unless the repo gains one
- [X] T002 [P] Add `AppData/uploads/` to `.gitignore` at the repository root
- [X] T003 [P] Add `"FileStorage": { "RootPath": "AppData/uploads" }` to `ContosoDashboard/appsettings.json`
- [X] T004 [P] Create SQLite in-memory `ApplicationDbContext` factory (enforces FK/check constraints; one open connection per test) in `ContosoDashboard.Tests/TestSupport/TestDbFactory.cs`
- [X] T005 [P] Create test fakes in `ContosoDashboard.Tests/TestSupport/FakeFileStorageService.cs` (in-memory, with switchable `FailOnUpload`/`FailOnDelete`) and `ContosoDashboard.Tests/TestSupport/FakeMalwareScanService.cs` (switchable clean/infected)
- [X] T006 [P] Create seed helper in `ContosoDashboard.Tests/TestSupport/TestData.cs` that builds users (Employee, TeamLead, ProjectManager, Administrator across two departments), projects, and `ProjectMember` rows used by every test class

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, schema, storage/scan infrastructure, shared rules and DTOs that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Entities and schema

- [X] T007 [P] Create `ContosoDashboard/Models/Document.cs` with `Document` and `DocumentCategory` enum (exactly: ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other). Fields per data-model.md: `DocumentId` int PK identity; `Title` string(255) required, trimmed, non-empty; `Description` string(2000)? optional; `Category` required; `OriginalFileName` string(255) display/download name only, sanitized, never used in a storage path; `FileType` string(10) canonical lowercase extension from allow-list; `ContentType` string(100) derived server-side; `FileSizeBytes` long 1…26,214,400; `StoragePath` string(500) provider-neutral key `{yyyy}/{MM}/{Guid:N}.{ext}`, unique; `UploadDate` DateTime UTC set by server; `UploadedByUserId` int FK→User required (Restrict); `ProjectId` int? FK→Project optional, at most one (NoAction). Navigations: `UploadedByUser`, `Project?`, `Tags`, `Shares`, `TaskLinks`
- [X] T008 [P] Create `ContosoDashboard/Models/DocumentTag.cs`: `DocumentTagId` PK; `DocumentId` FK→Document (cascade); `Tag` string(30) trimmed, lowercased, non-empty, unique per document
- [X] T009 [P] Create `ContosoDashboard/Models/DocumentShare.cs`: `DocumentShareId` PK; `DocumentId` FK→Document (cascade); `SharedWithUserId` int? FK→User (Restrict, set for a user share); `SharedWithTeam` string(100)? department name (set for a team share); `SharedByUserId` int FK→User (Restrict, always the document owner); `SharedDate` DateTime UTC
- [X] T010 [P] Create `ContosoDashboard/Models/DocumentActivity.cs` with `DocumentActivity` and `DocumentActionType` enum (Upload, Download, Preview, Delete, Share, ShareRemoved). Fields: `DocumentActivityId` PK; `DocumentId` int with **no FK** so records outlive the document (FR-042); `DocumentTitle` string(255) snapshot; `FileType` string(10) snapshot; `ActionType`; `ActorUserId` int FK→User (Restrict); `DocumentOwnerUserId` int snapshot, no FK; `TargetDescription` string(255)? (recipient user name or team name for Share/ShareRemoved); `Timestamp` DateTime UTC. Append-only: no update/delete code paths
- [X] T011 [P] Create `ContosoDashboard/Models/TaskDocument.cs`: composite PK (`TaskId` FK→TaskItem cascade, `DocumentId` FK→Document cascade); `AttachedByUserId` int FK→User (Restrict); `AttachedDate` DateTime UTC
- [X] T012 [P] Append `DocumentShared` and `DocumentAdded` (at the END, keeping existing persisted integer values valid) to `NotificationType` in `ContosoDashboard/Models/Notification.cs`
- [X] T013 [P] Add navigation collections only: `ICollection<Document> UploadedDocuments` in `ContosoDashboard/Models/User.cs`, `ICollection<Document> Documents` in `ContosoDashboard/Models/Project.cs`, `ICollection<TaskDocument> DocumentLinks` in `ContosoDashboard/Models/TaskItem.cs`
- [X] T014 Update `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T007–T013): add DbSets (`Documents`, `DocumentTags`, `DocumentShares`, `DocumentActivities`, `TaskDocuments`); relationships and delete behaviors per data-model.md (Document→UploadedBy Restrict, Document→Project NoAction, tags/shares/task links cascade from Document, TaskDocument→TaskItem cascade; avoid multiple cascade paths); indexes `Document(UploadedByUserId)`, `(ProjectId)`, `(UploadDate)`, `(Category)`, unique `(StoragePath)`, unique `DocumentTag(DocumentId, Tag)` plus `DocumentTag(Tag)`, `DocumentActivity(ActionType, Timestamp)`, `(DocumentId)`, `(ActorUserId)`; check constraint on `DocumentShare` — exactly one of `SharedWithUserId` / `SharedWithTeam` is non-null; filtered unique indexes `(DocumentId, SharedWithUserId)` where not null and `(DocumentId, SharedWithTeam)` where not null

### Infrastructure behind interfaces (Constitution II)

- [X] T015 [P] Create `ContosoDashboard/Services/IFileStorageService.cs` (`UploadAsync`, `DownloadAsync` throws `FileNotFoundException` when missing, `DeleteAsync` idempotent, `GetUrlAsync` returns null locally) and `ContosoDashboard/Services/LocalFileStorageService.cs`: resolves keys under `FileStorage:RootPath` (default `AppData/uploads`, outside `wwwroot`), throws if the resolved full path is outside the root, never overwrites an existing key, creates root/subdirectories lazily, streams with `FileOptions.Asynchronous | SequentialScan`
- [X] T016 [P] Create `ContosoDashboard/Services/IMalwareScanService.cs` (`ScanAsync(Stream, string fileName, CancellationToken)` → `MalwareScanResult(bool IsClean, string? ThreatName)`) and `ContosoDashboard/Services/StubMalwareScanService.cs` (offline; flags the EICAR test string, otherwise clean)
- [X] T017 [P] Create `ContosoDashboard/Services/DocumentFileRules.cs`: allow-list `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`; max 26,214,400 bytes; zero-byte rejected; magic-byte checks (PDF `%PDF-`; JPEG `FF D8 FF`; PNG `89 50 4E 47 0D 0A 1A 0A`; legacy Office OLE2 `D0 CF 11 E0 A1 B1 1A E1`; OOXML ZIP `50 4B 03 04` plus `ZipArchive` check for root part `word/`, `xl/`, `ppt/`; `.txt` rejects NUL bytes in first 8 KB / invalid UTF-8/ASCII/UTF-16 BOM text); server-derived content type from canonical extension; filename sanitizer for display/`Content-Disposition`; user-presentable messages ("25 MB", "PDF, Word, Excel, PowerPoint, text, JPEG, PNG")
- [X] T018 [P] Create shared DTOs/records in `ContosoDashboard/Services/DocumentModels.cs`: `UploadRequest`, `FileUpload { Stream Content; string FileName; long Length; IProgress<int>? Progress }`, `DocumentMetadata { Title, Description?, Category, ProjectId?, Tags }`, `DocumentQuery { Category?, ProjectId?, DateFrom?, DateTo?, SortBy (Title|UploadDate|Category|FileSize), Descending, Page, PageSize }`, `PagedResult<T>`, `OperationResult`, `UploadResult : OperationResult { DocumentId? }`, `DocumentStream`, `DocumentAccessKind { Download, Preview }`, `DocumentListItem` (incl. `IsPreviewable` derived from `FileType` in pdf/jpg/jpeg/png), `DocumentDetails` (capability flags `CanEdit`, `CanDelete`, `CanReplace`, `CanShare`), `DocumentShareInfo`

### Shared service rules

- [X] T019 Create `ContosoDashboard/Services/IDocumentActivityService.cs` and `ContosoDashboard/Services/DocumentActivityService.cs` (depends on T010, T014): append-only `RecordAsync(DocumentActionType, documentId, title, fileType, ownerUserId, actorUserId, target?)`; no update/delete members
- [X] T020 Create `ContosoDashboard/Services/DocumentAccessRules.cs` (depends on T014): the single central, live-evaluated (from the requester's current DB role/department, not claims) rules — an `IQueryable<Document>` **visibility** predicate (uploader; Administrator; document's project manager; member of document's project; direct user share; team share where `SharedWithTeam == user.Department`; `UserRole.TeamLead` whose department equals the uploader's) and pure **management** methods: edit metadata/delete (uploader, Administrator, project manager of the document's project, TeamLead with same department as uploader); replace/share/unshare (uploader only); project association (member/manager of that project, Administrator any)
- [X] T021 Create `ContosoDashboard/Services/IDocumentService.cs` with the full `IDocumentService` interface from contracts/service-interfaces.md (upload, update, replace, delete, queries, `OpenReadAsync`, task integration members); every method takes the requesting user id (depends on T018)
- [X] T022 Register in `ContosoDashboard/Program.cs`: `AddSingleton<IFileStorageService, LocalFileStorageService>()`, `AddSingleton<IMalwareScanService, StubMalwareScanService>()`, `AddScoped<IDocumentActivityService, DocumentActivityService>()` (do NOT alter security headers, CSP, or the 8-hour sliding cookie)

### Foundational tests

- [X] T023 [P] Tests in `ContosoDashboard.Tests/DocumentFileRulesTests.cs`: allow-list accept/reject, 25 MB boundary (26,214,400 ok, +1 rejected), zero-byte, each signature match, exe-renamed-to-.pdf mismatch, OOXML root-part check, text NUL rejection, filename sanitization (`../`, long names, special characters)
- [X] T024 [P] Tests in `ContosoDashboard.Tests/LocalFileStorageServiceTests.cs`: upload/download round-trip, path traversal (`../`, absolute paths) rejected, no overwrite of existing key, idempotent delete, missing file throws `FileNotFoundException`, `GetUrlAsync` returns null
- [X] T025 [P] Tests in `ContosoDashboard.Tests/DocumentAuthorizationTests.cs` covering `DocumentAccessRules`: full visibility/management matrix from data-model.md (success path per role) and unauthorized path (non-member, other-department Team Lead, ProjectManager of a different project, non-recipient, Administrator cannot replace/share), live evaluation after membership/department change
- [X] T026 Verify schema bootstrap: drop the dev LocalDB once (`dotnet ef database drop --force`), run the app, and confirm `EnsureCreated()` builds all new tables with no multiple-cascade-path error and the existing seed still loads (Constitution V; research R14)

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Upload a Document with Metadata (Priority: P1) 🎯 MVP

**Goal**: An employee uploads one or many files with title, category, optional description/project/tags, sees progress and a clear result per file, and the system records uploader, date/time, size, type.

**Independent Test**: Sign in as an Employee, upload a valid PDF with a title and category; a success message appears and the document row holds the correct uploader, UTC date/time, size, and type. Oversize, wrong type, signature mismatch, zero-byte, EICAR, and non-member-project uploads are rejected with nothing stored.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [X] T027 [P] [US1] Upload tests in `ContosoDashboard.Tests/DocumentServiceTests.cs`: success captures uploader/date/size/type and a GUID-based `StoragePath` (never the user filename); missing title/category rejected; >25 MB, unsupported type, signature mismatch, zero-byte, EICAR rejected with nothing stored; same-name files both stored without overwrite; tags normalized (trim, lowercase, dedupe, ≤10 tags of ≤30 chars); storage failure leaves no record/file; DB-save failure triggers compensating file delete; project association refused for non-member (unauthorized path) but allowed for member/manager/Administrator

### Implementation for User Story 1

- [X] T028 [US1] Create `ContosoDashboard/Services/DocumentService.cs` (`IDocumentService`) and implement `UploadAsync`: validate (`DocumentFileRules`) → malware scan → generate GUID path `{yyyy}/{MM}/{Guid:N}{canonicalExt}` → `IFileStorageService.UploadAsync` → save row + tags + `Upload` activity in one `SaveChanges`; on any failure delete the stored file and persist nothing; enforce project association rule via `DocumentAccessRules`; sanitize `OriginalFileName`; user-presentable failure messages; report progress via `FileUpload.Progress`. (Other interface members may throw `NotImplementedException` until their stories.)
- [X] T029 [US1] Register `AddScoped<IDocumentService, DocumentService>()` in `ContosoDashboard/Program.cs`
- [X] T030 [P] [US1] Create `ContosoDashboard/Shared/DocumentUploadForm.razor` (parameters `int? ProjectId`, `int? TaskId`, `EventCallback OnUploaded`): `InputFile multiple` (accept list as hint only) with `OpenReadStream(maxAllowedSize: 25MB + 1)` copied in 80 KB chunks to a buffer; per-file editable Title pre-filled from file name without extension; one shared required Category, Description, Project dropdown (only the user's projects via existing `IProjectService.GetUserProjectsAsync`), Tags; per-file Bootstrap progress bar and success/error result (valid files upload, invalid reported individually); inline validation of missing title/category; form resets after completion; text explaining where files appear, who can see them, and that deletion is permanent (FR-046); calls `IDocumentService` only; no `MarkupString`
- [X] T031 [US1] Create `ContosoDashboard/Pages/Documents.razor` at `/documents` with `[Authorize(Policy = "Employee")]`, user id from `NameIdentifier`, page header and the upload panel hosting `DocumentUploadForm` (≤3 clicks: Upload → choose files → Submit, FR-012)
- [X] T032 [US1] Add "My Documents" (`bi-file-earmark-text`) link to `ContosoDashboard/Shared/NavMenu.razor`

**Checkpoint**: User Story 1 is functional and testable independently (upload + validation + recording)

---

## Phase 4: User Story 2 - Browse, Sort, Filter, and Search My Documents (Priority: P1)

**Goal**: A user lists their uploaded documents with title/category/date/size/project, sorts, filters, and searches; results never include or reveal inaccessible documents.

**Independent Test**: With several documents across categories/projects, sort by each field, apply each filter (alone and combined), search by title, description, tag, uploader, and project; results are correct and limited to accessible documents; empty state shown when none.

### Tests for User Story 2 ⚠️

- [X] T033 [P] [US2] Query tests in `ContosoDashboard.Tests/DocumentQueryTests.cs`: `GetMyDocumentsAsync` returns only the uploader's documents; sort by title/date/category/size asc+desc; filter by category, project, date range, and combined; `SearchAsync` matches title, description, tag, uploader name, project name; search results and `TotalCount` exclude documents the user cannot access (unauthorized path — no existence leak); paging (page size 25)

### Implementation for User Story 2

- [X] T034 [US2] Implement `GetMyDocumentsAsync` and `SearchAsync` in `ContosoDashboard/Services/DocumentService.cs`: server-side filter/sort/paging in SQL, search AND-ed with the `DocumentAccessRules` visibility predicate (`EF.Functions.Like`/`Contains` over title, description, tags, uploader `DisplayName`, project `Name`), separate count query, projection to `DocumentListItem` (no heavy `Include` graphs), page size 25
- [X] T035 [P] [US2] Create `ContosoDashboard/Shared/DocumentList.razor` (parameters `DocumentQuery`, `PagedResult<DocumentListItem>`, `bool ShowOwnerActions`, callbacks): table with sortable headers (title, upload date, category, size), pager, project and size columns, empty-state slot
- [X] T036 [US2] Extend `ContosoDashboard/Pages/Documents.razor` (depends on T031, T035): list via `DocumentList`, sort controls, filters (category, project, date range), keyword search box, friendly empty state explaining how to upload the first document, refresh list after `OnUploaded`

**Checkpoint**: User Stories 1 AND 2 work independently

---

## Phase 5: User Story 3 - Download and Preview a Document (Priority: P1)

**Goal**: Users download any accessible document; PDFs/images preview in the browser; unauthorized access by any means (direct link, ID guessing) is denied with an identical 404.

**Independent Test**: Upload a PDF and a PNG; preview and download each; a second user with no access gets 404 for both; Office/text files show Download only with "Preview unavailable".

### Tests for User Story 3 ⚠️

- [X] T037 [P] [US3] Delivery tests in `ContosoDashboard.Tests/DocumentDeliveryTests.cs`: `OpenReadAsync` returns original bytes/name/server-derived content type for download and preview; records exactly one `Download` or `Preview` activity (distinct) per authorized request; returns null identically for missing id, non-viewable user, and non-previewable type on Preview; missing stored file returns null and logs an error; `GetDocumentAsync` returns null for inaccessible and correct capability flags (`CanEdit`, `CanDelete`, `CanReplace`, `CanShare`) for each role
- [X] T041 [P] [US3] Endpoint tests in `ContosoDashboard.Tests/DocumentEndpointTests.cs` using `WebApplicationFactory` for unauthenticated → login challenge and unauthorized == missing → identical empty 404; if the cost is not acceptable, cover via T037 plus quickstart manual scenarios and note it in the file header

### Implementation for User Story 3

- [X] T038 [US3] Implement `OpenReadAsync` and `GetDocumentAsync` in `ContosoDashboard/Services/DocumentService.cs`: visibility check via `DocumentAccessRules`, null for missing/denied/not-previewable, record `Download`/`Preview` activity before streaming, stream via `IFileStorageService.DownloadAsync`, log `Error` when the stored file is missing; `GetDocumentAsync` populates capability flags
- [X] T039 [US3] Map `GET /documents/{id:int}/download` and `GET /documents/{id:int}/preview` in `ContosoDashboard/Program.cs` per contracts/http-endpoints.md: `RequireAuthorization("Employee")`, user id from `NameIdentifier`, `Content-Disposition` attachment/inline with sanitized `filename` + `filename*=UTF-8''`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, `Content-Length`; preview only for pdf/jpg/jpeg/png; identical empty 404 for missing, unauthorized, non-previewable, and missing-file; no storage-path/GUID addressing; global `X-Frame-Options: DENY` unchanged
- [X] T040 [US3] Update `ContosoDashboard/Shared/DocumentList.razor`: Download as plain link to `/documents/{id}/download`; Preview link (`target="_blank" rel="noopener"`) to `/documents/{id}/preview` only when `IsPreviewable`, otherwise a "Preview unavailable" indicator

**Checkpoint**: User Stories 1–3 deliver the complete upload → find → retrieve loop (MVP)

---

## Phase 6: User Story 4 - Project Documents and Role-Based Access (Priority: P2)

**Goal**: Project pages list the project's documents for members/manager; Team Leads see/manage their team's documents; Administrators see all; personal documents stay private.

**Independent Test**: Project with a manager, two members, one non-member: manager uploads a project document; members view/download; the non-member cannot; a Team Lead sees team members' documents; an Administrator sees all; a personal-file document is invisible to others.

### Tests for User Story 4 ⚠️

- [ ] T042 [P] [US4] Tests in `ContosoDashboard.Tests/ProjectDocumentTests.cs`: members/manager see project documents; non-member gets empty list (unauthorized path); Employee member and Project Manager can upload to the project; Team Lead sees same-department uploads but not other departments'; Administrator sees all; Personal Files without project invisible to non-owners unless shared; removed member keeps own uploads but loses others' project documents; document stays accessible to owner/Administrator when its project is gone from the user's membership

### Implementation for User Story 4

- [ ] T043 [US4] Implement `GetProjectDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` (empty result unless requester is member/manager/Administrator of the project; same filter/sort/paging as T034)
- [ ] T044 [P] [US4] Create `ContosoDashboard/Shared/ProjectDocuments.razor` (parameter `int ProjectId`): lists documents via `DocumentList`, upload button hosting `DocumentUploadForm ProjectId=...` for members/manager, empty state
- [ ] T045 [US4] Modify `ContosoDashboard/Pages/ProjectDetails.razor` to add a Documents section/tab using `ProjectDocuments`

**Checkpoint**: Role-based visibility verified across all roles

---

## Phase 7: User Story 5 - Edit, Replace, and Delete Documents (Priority: P2)

**Goal**: Uploader (and Admin/PM/Team Lead per rules) edits metadata; only the uploader replaces the file; authorized users delete after explicit confirmation, permanently removing record and file.

**Independent Test**: Upload, edit each metadata field, replace the file and confirm the new content downloads, delete with confirmation and verify it is permanently gone; unauthorized users are refused.

### Tests for User Story 5 ⚠️

- [ ] T046 [P] [US5] Tests in `ContosoDashboard.Tests/DocumentManagementTests.cs`: edit succeeds for uploader/Admin/project manager/same-department Team Lead and is refused for project member, other-department Team Lead, and share recipient; replace succeeds only for uploader (Administrator/PM/Team Lead refused), keeps `DocumentId`/metadata/shares, applies same validation, new file under new GUID and old file deleted; failed replace (validation or storage) leaves original file and metadata intact; delete removes row, tags, shares, task links, and stored file and writes a `Delete` activity, with `DocumentActivity` rows retained (FR-042); delete refused for unauthorized users; delete by Project Manager of another member's document succeeds

### Implementation for User Story 5

- [ ] T047 [US5] Implement `UpdateMetadataAsync` in `ContosoDashboard/Services/DocumentService.cs` (management rule from `DocumentAccessRules`; title required non-empty ≤255, description ≤2000, category required, tag normalization; project association rule if changed; returns `OperationResult` without disclosing existence to unauthorized users)
- [ ] T048 [US5] Implement `ReplaceFileAsync` in `ContosoDashboard/Services/DocumentService.cs` (uploader only; same validate→scan→GUID path→store flow; update `FileType`, `ContentType`, `FileSizeBytes`, `StoragePath`; delete old file after commit; on failure delete the new file and keep the original intact)
- [ ] T049 [US5] Implement `DeleteAsync` in `ContosoDashboard/Services/DocumentService.cs` (management rule; remove row — cascades tags/shares/task links — and write `Delete` activity in the same `SaveChanges`, then delete the stored file idempotently; log if file removal fails)
- [ ] T050 [P] [US5] Create `ContosoDashboard/Shared/DocumentEditModal.razor` (parameter `int DocumentId`): title, description, category, tags, and Replace file control shown only when `CanReplace` (service re-checks)
- [ ] T051 [P] [US5] Create `ContosoDashboard/Shared/ConfirmDeleteModal.razor` (title, message): explicit confirmation stating deletion is permanent (FR-029/046); cancel leaves everything unchanged
- [ ] T052 [US5] Wire Edit/Replace/Delete row actions in `ContosoDashboard/Shared/DocumentList.razor` and `ContosoDashboard/Pages/Documents.razor` (depends on T050, T051, T036): buttons rendered from `DocumentDetails` capability flags, list/count refresh after changes

**Checkpoint**: Full lifecycle of a document works with correct authorization

---

## Phase 8: User Story 6 - Share Documents and Receive Notifications (Priority: P3)

**Goal**: Owners share with users/teams and can withdraw shares; recipients are notified and see "Shared with Me"; project members are notified of new project documents.

**Independent Test**: Share a document with one user and one team; each recipient is notified and sees it under Shared with Me and can preview/download; a non-recipient cannot; removing a share ends access immediately.

### Tests for User Story 6 ⚠️

- [ ] T053 [P] [US6] Tests in `ContosoDashboard.Tests/DocumentSharingTests.cs`: owner shares with users/team (recipients notified, owner excluded, `Share` activity); non-owner (incl. Administrator and recipient) refused; self-share refused; unknown user/department refused; duplicate shares prevented; `RemoveShareAsync` owner-only, immediate loss of view/preview/download and `ShareRemoved` activity; team share follows live department membership (user joins/leaves); `GetSharedWithMeAsync` shows direct + team shares and drops items when the document is deleted; shares grant view/preview/download only (edit/replace/delete/re-share refused); new project document notifies all members and manager except the uploader and a notification failure does not fail the upload

### Implementation for User Story 6

- [ ] T054 [P] [US6] Add `GetDepartmentsAsync()` (distinct non-empty `User.Department`) and `GetUsersForSharingAsync(int requestingUserId)` (id, display name, department only) to `ContosoDashboard/Services/UserService.cs` and its interface
- [ ] T055 [P] [US6] Create `ContosoDashboard/Services/IDocumentSharingService.cs` and `ContosoDashboard/Services/DocumentSharingService.cs`: `ShareWithUsersAsync`, `ShareWithTeamAsync` (department must be known; notifies current team members except owner), `RemoveShareAsync`, `GetSharesAsync` (owner only, else empty); notifications via existing `INotificationService` with `DocumentShared`; `Share`/`ShareRemoved` activity logged in the same `SaveChanges` where possible; recipients must exist; cannot share with self
- [ ] T056 [US6] Register `AddScoped<IDocumentSharingService, DocumentSharingService>()` in `ContosoDashboard/Program.cs`
- [ ] T057 [US6] Implement `GetSharedWithMeAsync` in `ContosoDashboard/Services/DocumentService.cs` (direct user shares + team shares matching the user's current department; excludes own documents; same filter/sort/paging)
- [ ] T058 [US6] Add best-effort post-commit notification (`DocumentAdded`) to project members and manager excluding the uploader in `UploadAsync` in `ContosoDashboard/Services/DocumentService.cs`; failures are logged and never fail the upload (FR-034)
- [ ] T059 [P] [US6] Create `ContosoDashboard/Shared/DocumentShareModal.razor` (parameter `int DocumentId`): pick users and/or a team, list current shares with remove action (owner only)
- [ ] T060 [P] [US6] Create `ContosoDashboard/Pages/SharedDocuments.razor` at `/documents/shared` with `[Authorize(Policy = "Employee")]`: "Shared with Me" list with Preview/Download only
- [ ] T061 [US6] Add "Shared with Me" (`bi-share`) link to `ContosoDashboard/Shared/NavMenu.razor`
- [ ] T062 [US6] Wire the Share row action (owner only, from `CanShare`) in `ContosoDashboard/Shared/DocumentList.razor` and `ContosoDashboard/Pages/Documents.razor` to `DocumentShareModal`

**Checkpoint**: Sharing and notifications verified

---

## Phase 9: User Story 7 - Task and Dashboard Integration (Priority: P3)

**Goal**: A task detail page shows, attaches, and uploads documents (adopting the task's project); the dashboard shows a Recent Documents widget and a document count card.

**Independent Test**: Open a task, upload a document from it; it appears on the task and under the task's project; the dashboard lists it under Recent Documents and the count increments; attaching a document from a different project is refused with a clear message.

### Tests for User Story 7 ⚠️

- [ ] T063 [P] [US7] Tests in `ContosoDashboard.Tests/TaskAttachmentTests.cs`: attach adopts the task's project when document has none; attach refused (clear message, nothing changes) when the document belongs to a different project; attach refused when user cannot view the document or is not a member/manager of the task's project (Administrator excepted); detach allowed for attacher/uploader/task's project manager/Administrator and never deletes the document; `GetAttachableDocumentsAsync` returns only accessible documents with no project or same project; `GetTaskDocumentsAsync` unauthorized for users who cannot access the task; `GetRecentDocumentsAsync` returns the uploader's 5 newest first; `GetDocumentCountAsync` counts only the uploader's documents and updates after upload/delete

### Implementation for User Story 7

- [ ] T064 [US7] Implement `GetTaskDocumentsAsync`, `AttachToTaskAsync` (FR-037 rules; sets `ProjectId` once if null; clear refusal message on project mismatch), `DetachFromTaskAsync`, and `GetAttachableDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`, using existing `ITaskService.GetTaskByIdAsync(taskId, userId)` for task access
- [ ] T065 [US7] Implement `GetRecentDocumentsAsync` (uploader's own, newest first, default 5) and `GetDocumentCountAsync` (uploader's own) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T066 [US7] Add `int DocumentCount` to `DashboardSummary` and populate it in `ContosoDashboard/Services/DashboardService.cs` (count of the requesting user's uploaded documents)
- [ ] T067 [P] [US7] Create `ContosoDashboard/Shared/TaskDocuments.razor` (parameter `int TaskId`): list task documents, attach existing (dropdown from `GetAttachableDocumentsAsync`), upload new via `DocumentUploadForm TaskId=...` (upload then `AttachToTaskAsync`, so the document adopts the task's project), detach, display the project-mismatch refusal message
- [ ] T068 [US7] Create `ContosoDashboard/Pages/TaskDetails.razor` at `/tasks/{taskId:int}` with `[Authorize(Policy = "Employee")]`: minimal task info via `ITaskService.GetTaskByIdAsync` (not-found/unauthorized shows a generic message) plus `TaskDocuments`
- [ ] T069 [US7] Replace the `ViewTaskDetails` TODO in `ContosoDashboard/Pages/Tasks.razor` with `NavigationManager` navigation to `/tasks/{taskId}`
- [ ] T070 [P] [US7] Create `ContosoDashboard/Shared/RecentDocumentsWidget.razor` (no parameters): last 5 uploads newest first, empty state, Preview/Download links
- [ ] T071 [US7] Modify `ContosoDashboard/Pages/Index.razor`: add `RecentDocumentsWidget` and a fifth summary card showing `DocumentCount` (zero when none)

**Checkpoint**: Documents surface where employees already work

---

## Phase 10: User Story 8 - Activity Tracking and Administrator Reports (Priority: P3)

**Goal**: Every upload/download/preview/delete/share/share-removal is recorded (retained after document deletion); Administrators view three reports and the activity log; others are denied.

**Independent Test**: Perform an upload, download, preview, share, unshare, and delete; as Administrator confirm each appears in the activity log and the three reports reflect it; a non-Administrator is denied at page and service.

### Tests for User Story 8 ⚠️

- [ ] T072 [P] [US8] Tests in `ContosoDashboard.Tests/DocumentReportTests.cs`: all six action types produce exactly one activity record (SC-010); records survive document deletion; uploads-by-type and top-uploaders derive from `Upload` rows and stay correct after deletions; access patterns count Download and Preview separately by document, user, and day over the date range including owner accesses; non-Administrator calls to every report method throw `UnauthorizedAccessException` (unauthorized path); date-range filtering

### Implementation for User Story 8

- [ ] T073 [P] [US8] Create `ContosoDashboard/Services/IDocumentReportService.cs` and `ContosoDashboard/Services/DocumentReportService.cs` with `GetUploadsByTypeAsync`, `GetTopUploadersAsync`, `GetAccessPatternsAsync`, `GetActivityAsync` (paged); verify the requester is `UserRole.Administrator` from the DB and **throw `UnauthorizedAccessException`** otherwise (decision recorded here per contract); SQL `GroupBy` over `DocumentActivity`; add row/report DTOs (`UploadsByTypeRow`, `TopUploaderRow`, `AccessPatternReport`, `DocumentActivityRow`) to `ContosoDashboard/Services/DocumentModels.cs`
- [ ] T074 [US8] Register `AddScoped<IDocumentReportService, DocumentReportService>()` in `ContosoDashboard/Program.cs`
- [ ] T075 [US8] Create `ContosoDashboard/Pages/DocumentReports.razor` at `/documents/reports` with `[Authorize(Policy = "Administrator")]`: uploads by type, top uploaders, access patterns with selectable date range (by document, user, day; Download vs Preview), paged activity log
- [ ] T076 [US8] Add "Document Reports" (`bi-bar-chart`) link visible only to Administrators in `ContosoDashboard/Shared/NavMenu.razor` (UI convenience only)

**Checkpoint**: All user stories independently functional

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, performance, and constitution quality gates

- [ ] T077 [P] Update `README.md`: features, routes (`/documents`, `/documents/shared`, `/documents/reports`, `/tasks/{id}`, download/preview endpoints), project structure, roles/access matrix, and Known Limitations (stub malware scanner is a simulation, local file storage, `EnsureCreated` requires a one-time database drop); note the README net8.0/net10.0 discrepancy only if touched
- [ ] T078 [P] Add performance check in `ContosoDashboard.Tests/DocumentPerformanceTests.cs`: seed 500 documents and assert list/search complete within 2 s (FR-044); manually time a 25 MB upload (≤30 s) and PDF/image preview (≤3 s) per quickstart
- [ ] T079 Security audit of all three layers (Constitution IV): confirm policy on both endpoints, `[Authorize]` on every new page, `requestingUserId` check on every new service method, no `MarkupString`, no file addressed by name/GUID, no weakened headers/CSP/cookie, no secrets committed
- [ ] T080 Build with no new warnings (nullable stays enabled), run the full `ContosoDashboard.Tests` suite, and confirm the app starts and seeds on a fresh LocalDB
- [ ] T081 Run all scenarios in `specs/001-document-management/quickstart.md` plus the existing README security scenarios (authentication required, user isolation, IDOR, role-based features)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Stories (Phases 3–10)**: All depend on Foundational completion
  - US1 → US2 → US3 form the P1 chain (US2/US3 extend `DocumentService.cs`, `DocumentList.razor`, `Documents.razor` created in US1/US2)
  - US4–US8 depend only on Foundational plus the shared files above (`DocumentService.cs`, `DocumentList.razor`, `Documents.razor`), so schedule them after US1–US3 to avoid same-file conflicts
- **Polish (Phase 11)**: Depends on all desired stories

### User Story Dependencies

- **US1 (P1)**: After Foundational — no story dependencies
- **US2 (P1)**: After Foundational; uses `Documents.razor` from US1 (T031)
- **US3 (P1)**: After Foundational; `DocumentList.razor` from US2 (T035)
- **US4 (P2)**: After US3 for usable lists (uses `DocumentList`, `DocumentUploadForm`)
- **US5 (P2)**: After US3 (uses `DocumentList`, `Documents.razor`)
- **US6 (P3)**: After US3; notification hook (T058) extends US1's `UploadAsync`
- **US7 (P3)**: After US3; independent of US4–US6 except shared files
- **US8 (P3)**: Logging is already produced by earlier stories; reports need only Foundational (T019) but meaningful end-to-end tests need US1/US3/US5/US6 actions

### Within Each User Story

- Tests first and failing → service methods → components → pages/wiring
- Tasks editing the same file (`DocumentService.cs`, `Program.cs`, `NavMenu.razor`, `DocumentList.razor`, `Documents.razor`) are sequential, not [P]

### Parallel Opportunities

- Setup: T002–T006 together after T001
- Foundational: T007–T013 (distinct model files) together; T015–T018 together; T023–T025 together once their targets exist
- Per story: the [P] test task and [P] component tasks (e.g., T044, T050/T051, T059/T060, T067/T070) can run alongside service work in a different file
- After US3, US4–US8 can be staffed in parallel if same-file edits to `DocumentService.cs`/`Program.cs`/`NavMenu.razor` are coordinated

---

## Parallel Example: Foundational Entities

```text
Task: "Create Document.cs and DocumentCategory in ContosoDashboard/Models/Document.cs"        (T007)
Task: "Create DocumentTag.cs in ContosoDashboard/Models/DocumentTag.cs"                       (T008)
Task: "Create DocumentShare.cs in ContosoDashboard/Models/DocumentShare.cs"                   (T009)
Task: "Create DocumentActivity.cs in ContosoDashboard/Models/DocumentActivity.cs"             (T010)
Task: "Create TaskDocument.cs in ContosoDashboard/Models/TaskDocument.cs"                     (T011)
```

## Parallel Example: User Story 1

```text
Task: "Upload tests in ContosoDashboard.Tests/DocumentServiceTests.cs"                        (T027)
Task: "DocumentUploadForm.razor in ContosoDashboard/Shared/DocumentUploadForm.razor"         (T030, alongside T028 service work)
```

---

## Implementation Strategy

### MVP First (User Stories 1–3, the P1 slice) — **T001–T041**

The three P1 stories form one inseparable value loop (store → find → retrieve): US1 alone stores files nobody can list or open, so the MVP is Setup + Foundational + US1 + US2 + US3.

1. Complete Phase 1: Setup (**T001–T006**)
2. Complete Phase 2: Foundational (**T007–T026**) — CRITICAL, blocks all stories
3. Complete Phase 3: US1 Upload (**T027–T032**) → **STOP and validate** upload and rejection paths (earliest demoable checkpoint)
4. Complete Phase 4: US2 Browse/Search (**T033–T036**)
5. Complete Phase 5: US3 Download/Preview (**T037–T041**)
6. **STOP and VALIDATE the MVP (T001–T041)**: run tests, then quickstart scenarios for upload, list/search, download/preview, and the unauthorized-404 checks; demo

### Incremental Delivery

1. MVP **T001–T041** → demo
2. Add US4 Project documents (**T042–T045**) and US5 Edit/Replace/Delete (**T046–T052**) → demo (P2)
3. Add US6 Sharing & notifications (**T053–T062**) → demo
4. Add US7 Task & dashboard integration (**T063–T071**) → demo
5. Add US8 Activity & reports (**T072–T076**) → demo
6. Polish (**T077–T081**)

Each story adds value without breaking previous stories.

### Parallel Team Strategy

1. Team completes Setup + Foundational together (T001–T026)
2. One developer finishes the P1 chain (T027–T041) to establish the shared files
3. Then: Developer A: US4 + US5; Developer B: US6; Developer C: US7; Developer D: US8 — coordinating edits to `DocumentService.cs`, `Program.cs`, `NavMenu.razor`

---

## Notes

- [P] tasks = different files, no dependency on incomplete tasks
- [Story] label maps each task to its user story for traceability
- Tests are included because Constitution V requires success + unauthorized coverage for new services; verify they fail before implementing
- Upload sequence (Constitution VI): generate unique path → save file → save metadata, with compensating file delete on failure
- Unauthorized and missing documents must be indistinguishable (identical 404 / null / empty) everywhere (FR-016)
- Commit after each task or logical group; stop at any checkpoint to validate the story independently
