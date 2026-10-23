# Data Model: Document Upload and Management

**Feature**: `001-document-management` | **Date**: 2026-10-22

Conventions follow the existing models: `int` identity keys, data annotations plus Fluent API in `ApplicationDbContext`, `DateTime` stored in UTC, nullable reference types enabled. Schema is created by `EnsureCreated()`; no seed rows are added for these entities.

## Entities

### Document (`Models/Document.cs`)

| Field | Type | Rules |
|---|---|---|
| `DocumentId` | int, PK | identity |
| `Title` | string(255) | required, trimmed, non-empty (FR-007) |
| `Description` | string(2000)? | optional |
| `Category` | `DocumentCategory` enum | required; exactly: ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other (FR-008) |
| `OriginalFileName` | string(255) | display/download name only; sanitized; **never** used in a storage path (FR-014) |
| `FileType` | string(10) | canonical lowercase extension from allow-list, e.g. `pdf` (FR-009) |
| `ContentType` | string(100) | derived server-side from `FileType`, not from client |
| `FileSizeBytes` | long | 1 … 26,214,400 (FR-003) |
| `StoragePath` | string(500) | provider-neutral key `{yyyy}/{MM}/{Guid:N}.{ext}`; unique |
| `UploadDate` | DateTime (UTC) | set by server (FR-009) |
| `UploadedByUserId` | int, FK→User | required; Restrict delete; the owner |
| `ProjectId` | int?, FK→Project | optional; at most one project; NoAction delete |

Navigation: `UploadedByUser`, `Project?`, `Tags`, `Shares`, `TaskLinks`.
Indexes: `UploadedByUserId`, `ProjectId`, `UploadDate`, `Category`, unique `StoragePath`.
Derived (not stored): uploader name (from `User.DisplayName`), `IsPreviewable` (`FileType` in pdf/jpg/jpeg/png).

### DocumentTag (`Models/DocumentTag.cs`)

| Field | Type | Rules |
|---|---|---|
| `DocumentTagId` | int, PK | |
| `DocumentId` | int, FK→Document | cascade delete |
| `Tag` | string(30) | trimmed, lowercased, non-empty; unique per document |

Index: unique `(DocumentId, Tag)`, plus `Tag` for search. Max 10 tags per document (service rule).

### DocumentShare (`Models/DocumentShare.cs`)

| Field | Type | Rules |
|---|---|---|
| `DocumentShareId` | int, PK | |
| `DocumentId` | int, FK→Document | cascade delete (US6.5) |
| `SharedWithUserId` | int?, FK→User | Restrict; set for a user share |
| `SharedWithTeam` | string(100)? | department name; set for a team share |
| `SharedByUserId` | int, FK→User | Restrict; always the document owner (FR-035) |
| `SharedDate` | DateTime (UTC) | |

Constraints: check constraint — exactly one of `SharedWithUserId` / `SharedWithTeam` is non-null; filtered unique indexes `(DocumentId, SharedWithUserId)` where not null and `(DocumentId, SharedWithTeam)` where not null. A user cannot share with themselves. Removal = hard delete of the row (history is in `DocumentActivity`).

### DocumentActivity (`Models/DocumentActivity.cs`) — append-only

| Field | Type | Rules |
|---|---|---|
| `DocumentActivityId` | int, PK | |
| `DocumentId` | int | **no FK** so records outlive the document (FR-042) |
| `DocumentTitle` | string(255) | snapshot at action time |
| `FileType` | string(10) | snapshot (for the type report) |
| `ActionType` | `DocumentActionType` enum | Upload, Download, Preview, Delete, Share, ShareRemoved (FR-040) |
| `ActorUserId` | int, FK→User | Restrict |
| `DocumentOwnerUserId` | int | snapshot of uploader (for the active-uploader report); no FK needed |
| `TargetDescription` | string(255)? | for Share/ShareRemoved: recipient user name or team name |
| `Timestamp` | DateTime (UTC) | |

Indexes: `(ActionType, Timestamp)`, `DocumentId`, `ActorUserId`. No update/delete code paths exist.

### TaskDocument (`Models/TaskDocument.cs`)

| Field | Type | Rules |
|---|---|---|
| `TaskId` | int, FK→TaskItem | composite PK part; cascade |
| `DocumentId` | int, FK→Document | composite PK part; cascade |
| `AttachedByUserId` | int, FK→User | Restrict |
| `AttachedDate` | DateTime (UTC) | |

Detaching removes only the link, never the document.

### Existing entities touched

- `User`: no schema change; `Department` is the "team". Add `ICollection<Document> UploadedDocuments`.
- `Project`: add `ICollection<Document> Documents`.
- `TaskItem`: add `ICollection<TaskDocument> DocumentLinks`.
- `NotificationType`: **append** `DocumentShared`, `DocumentAdded` (keeps existing persisted values valid).
- `DashboardSummary` (DTO): add `int DocumentCount`.

## Relationships

```text
User 1──* Document (UploadedBy)        Project 0..1──* Document
Document 1──* DocumentTag              Document 1──* DocumentShare ──> User | Department(team)
Document *──* TaskItem  (via TaskDocument)
DocumentActivity ··(DocumentId, no FK)·· Document      DocumentActivity *──1 User (actor)
```

## Validation Rules (enforced in `DocumentService`, not only UI)

1. Extension ∈ allow-list; size 1…25 MB; magic-byte/structure check matches extension; EICAR/scanner result clean.
2. Title and Category required; description ≤ 2000; tags normalized (trim, lowercase, dedupe), ≤10 tags of ≤30 chars.
3. `ProjectId` allowed only if requester is member/manager of that project (Administrator: any) (FR-010).
4. A document has at most one project; attaching to a task of a different project is refused; a project-less document adopts the task's project on attach (FR-037).
5. Replace: uploader only; same validation; keeps `DocumentId`, metadata, shares; updates `FileType`, `ContentType`, `FileSizeBytes`, `StoragePath`.
6. Share/Unshare: owner only; recipients must exist; team must be a known department.

## State Transitions

```text
(none) --Upload--> Active --Edit metadata--> Active
                   Active --Replace file--> Active (new StoragePath, old file deleted)
                   Active --Share/Unshare--> Active (DocumentShare rows added/removed)
                   Active --Attach/Detach--> Active (TaskDocument rows; ProjectId set once if null)
                   Active --Delete(confirmed)--> (removed: row, tags, shares, task links, stored file)
                                                 DocumentActivity rows remain
```

## Access Matrix (summary; authoritative logic in research R7)

| Action | Uploader | Admin | Project Mgr (doc's project) | Project member | Team Lead (same dept as uploader) | Direct/team share recipient |
|---|---|---|---|---|---|---|
| View / Preview / Download | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Edit metadata | ✔ | ✔ | ✔ | ✘ | ✔ | ✘ |
| Delete | ✔ | ✔ | ✔ | ✘ | ✔ | ✘ |
| Replace file | ✔ | ✘ | ✘ | ✘ | ✘ | ✘ |
| Share / Unshare | ✔ | ✘ | ✘ | ✘ | ✘ | ✘ |

## Seed Data / Bootstrap

No change to existing seed data. `Program.cs` continues to call `EnsureCreated()`. On first run after upgrade the developer must drop the existing LocalDB database once (see quickstart). The upload root directory is created lazily by `LocalFileStorageService`.
