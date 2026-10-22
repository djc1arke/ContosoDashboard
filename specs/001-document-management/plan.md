# Implementation Plan: Document Upload and Management

**Branch**: `001-document-management` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-document-management/spec.md`

## Summary

Add a secure document repository to ContosoDashboard: employees upload files (PDF, Office, text, JPEG/PNG up to 25 MB) with title, category, optional project/tags; browse, sort, filter, and search; preview PDFs/images and download; edit/replace/delete per role; share with users or departments; attach to tasks; see recent documents and counts on the dashboard; and Administrators get an immutable activity log with three reports.

Technical approach (details in [research.md](research.md)): new EF Core entities (`Document`, `DocumentTag`, `DocumentShare`, `DocumentActivity`, `TaskDocument`) in the existing LocalDB model; new scoped services behind interfaces (`IDocumentService`, `IDocumentSharingService`, `IDocumentActivityService`, `IDocumentReportService`); infrastructure behind `IFileStorageService` (local filesystem under `AppData/uploads`, GUID-based keys, outside `wwwroot`) and `IMalwareScanService` (offline EICAR-detecting stub); files delivered only through two authorization-checked minimal-API endpoints that return an identical 404 for missing and unauthorized; one central, live-evaluated visibility/management predicate drives lists, search, and actions; Blazor `InputFile` for upload with progress; new Razor pages/components plus a minimal task details page; a new xUnit test project covering success and unauthorized paths.

## Technical Context

**Language/Version**: C# on `net10.0` (per `ContosoDashboard.csproj`; nullable and implicit usings enabled)

**Primary Dependencies**: ASP.NET Core Blazor Server, Razor Pages (login/logout), EF Core 8.0.0 SqlServer (existing); no new runtime packages. Test project adds xUnit, `Microsoft.NET.Test.Sdk`, `Microsoft.EntityFrameworkCore.Sqlite` 8.0.x (tests only)

**Storage**: SQL Server LocalDB via `EnsureCreated()` for metadata; local filesystem (`FileStorage:RootPath`, default `AppData/uploads`) for file bytes via `IFileStorageService`

**Testing**: xUnit in new `ContosoDashboard.Tests` (SQLite in-memory + temp-dir storage); manual scenarios in [quickstart.md](quickstart.md) and existing README security scenarios

**Target Platform**: Windows/Linux/macOS dev machine running Kestrel with LocalDB (Windows); current desktop browsers

**Project Type**: Web application (single ASP.NET Core Blazor Server project)

**Performance Goals**: 25 MB upload ≤ 30 s; lists/search (≤ 500 docs) ≤ 2 s; preview ≤ 3 s (FR-043–045)

**Constraints**: Fully offline, no cloud dependency; files never under `wwwroot` and never addressed by user-supplied names; no weakening of security headers/CSP/cookie; no orphaned files/records on failure; identical 404 for missing vs. unauthorized

**Scale/Scope**: Training app; tens of users, ≤ 500 documents per list view; ~5 entities, 4 services + 2 infrastructure interfaces, 3 new pages (+1 task details page), 8 components, 2 HTTP endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.* Constitution v1.0.0.

| Principle | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Training-purpose, offline-first | PASS | PASS | Local FS + LocalDB only; stub scanner is offline and documented as a simulation in Known Limitations (R4); no new training limitation silently widened. |
| II. Infrastructure behind interfaces | PASS | PASS | `IFileStorageService` (`UploadAsync/DeleteAsync/DownloadAsync/GetUrlAsync`) + `LocalFileStorageService`; `IMalwareScanService` + stub; provider-neutral `StoragePath` key keeps schema stable for Azure Blob swap (R2, contracts). |
| III. Layered architecture & service contracts | PASS | PASS | Entities in `Models/`, DbSet/config in `ApplicationDbContext`, logic in `Services/` as scoped `I<Name>Service`; Razor components call services only; async, projections/`Include` to avoid N+1, indexes defined; nullable stays enabled. |
| IV. Defense-in-depth security | PASS | PASS | Layer 1 policies on endpoints/pages (`Employee`, `Administrator`); layer 2 `[Authorize]` on every new page; layer 3 service checks take `requestingUserId` on every method; existing four roles reused; allow-list + 25 MB + signature validation; GUID paths outside `wwwroot`; delivery only via authorized endpoints; headers/cookie untouched (preview opens in new tab rather than relaxing `X-Frame-Options`, R6). |
| V. Verifiable increments & tests | PASS | PASS | Spec has independent stories; new `ContosoDashboard.Tests` covers success + unauthorized for each service (R15); `EnsureCreated()`/seed continue to work (R14); README security scenarios retained in quickstart. |
| VI. Simplicity & consistency | PASS (with justified additions) | PASS | Existing conventions, Bootstrap 5.3/Icons; upload sequence validate → scan → generate path → save file → save metadata with compensating delete (R5); additions justified below. |

**Gate result**: No unjustified violations. Complexity-tracking entries below are additions, not violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-management/
├── plan.md                      # This file
├── research.md                  # Phase 0
├── data-model.md                # Phase 1
├── quickstart.md                # Phase 1
├── contracts/
│   ├── http-endpoints.md        # download / preview endpoints
│   ├── service-interfaces.md    # service + infrastructure interfaces, DI, config
│   └── ui-routes.md             # pages and components
├── checklists/                  # (existing)
└── tasks.md                     # Phase 2 (/speckit.tasks – NOT created here)
```

### Source Code (repository root)

```text
ContosoDashboard/                          # existing single Blazor Server project
├── Models/
│   ├── Document.cs                        # NEW (+ DocumentCategory enum)
│   ├── DocumentTag.cs                     # NEW
│   ├── DocumentShare.cs                   # NEW
│   ├── DocumentActivity.cs                # NEW (+ DocumentActionType enum)
│   ├── TaskDocument.cs                    # NEW
│   ├── Notification.cs                    # MOD: append DocumentShared, DocumentAdded
│   ├── User.cs / Project.cs / TaskItem.cs # MOD: navigation collections only
├── Data/ApplicationDbContext.cs           # MOD: DbSets, relationships, indexes, check constraint
├── Services/
│   ├── IFileStorageService.cs + LocalFileStorageService.cs        # NEW
│   ├── IMalwareScanService.cs + StubMalwareScanService.cs         # NEW
│   ├── DocumentFileRules.cs                                       # NEW (allow-list, size, signatures)
│   ├── DocumentService.cs (IDocumentService)                      # NEW
│   ├── DocumentSharingService.cs (IDocumentSharingService)        # NEW
│   ├── DocumentActivityService.cs (IDocumentActivityService)      # NEW
│   ├── DocumentReportService.cs (IDocumentReportService)          # NEW
│   ├── DashboardService.cs                # MOD: DocumentCount
│   └── UserService.cs                     # MOD: GetDepartmentsAsync, GetUsersForSharingAsync
├── Pages/
│   ├── Documents.razor                    # NEW  /documents
│   ├── SharedDocuments.razor              # NEW  /documents/shared
│   ├── DocumentReports.razor              # NEW  /documents/reports (Administrator)
│   ├── TaskDetails.razor                  # NEW  /tasks/{taskId:int}
│   ├── Tasks.razor                        # MOD: wire ViewTaskDetails
│   ├── ProjectDetails.razor               # MOD: documents section
│   └── Index.razor                        # MOD: Recent Documents widget + count card
├── Shared/
│   ├── DocumentUploadForm.razor, DocumentList.razor, DocumentEditModal.razor,
│   │   DocumentShareModal.razor, ConfirmDeleteModal.razor, ProjectDocuments.razor,
│   │   TaskDocuments.razor, RecentDocumentsWidget.razor           # NEW
│   └── NavMenu.razor                      # MOD: links
├── Program.cs                             # MOD: DI, two endpoints, FileStorage config
├── appsettings.json                       # MOD: FileStorage:RootPath
└── AppData/uploads/                       # runtime only; git-ignored

ContosoDashboard.Tests/                    # NEW xUnit project
├── DocumentServiceTests.cs, DocumentAuthorizationTests.cs, DocumentSharingTests.cs,
│   DocumentFileRulesTests.cs, LocalFileStorageServiceTests.cs,
│   DocumentReportTests.cs, TaskAttachmentTests.cs
└── TestSupport/ (SQLite in-memory context factory, fake storage/scanner, user seeding)
```

**Repo-level edits**: add `ContosoDashboard.Tests` to the solution (if a solution file exists), add `AppData/uploads/` to `.gitignore`, update `README.md` (features, routes, structure, roles, Known Limitations: stub scanner, local storage, `EnsureCreated` requires DB drop).

**Structure Decision**: Keep the existing single web project layout and add feature files in the existing `Models/`, `Services/`, `Pages/`, `Shared/` folders, plus one test project (required by Principle V).

## Complexity Tracking

No constitution violations. Additions that exceed the bare minimum, with justification:

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| New `ContosoDashboard.Tests` project | Principle V requires automated success/unauthorized tests for new services; none exists | Testing only manually would not satisfy the principle or catch IDOR regressions |
| Four service interfaces (document, sharing, activity, reports) rather than one | Keeps each file focused and lets reports/activity be admin-scoped independently | A single large service would mix owner, admin, and audit concerns, hurting testability |
| New task details page | FR-036 requires a task detail page; the existing `ViewTaskDetails` is a TODO | A modal on `Tasks.razor` does not match the spec's "task detail page" |
| `DocumentActivity` without FK to `Document` | FR-042 requires audit records to survive deletion | An FK with cascade would delete history; nullable FK would lose identity |
