# Contract: UI Routes and Components

**Feature**: `001-document-management` | Blazor Server, Bootstrap 5.3 + Bootstrap Icons. Every page has `[Authorize]` (layer 2) with the stated policy; each calls services that enforce layer 3.

## Pages

| Route | File | Policy | Purpose / Stories |
|---|---|---|---|
| `/documents` | `Pages/Documents.razor` | Employee | **My Documents**: list (title, category, upload date, size, project), sort (title/date/category/size), filter (category, project, date range), keyword search, empty state, upload panel, row actions (Preview, Download, Edit, Replace, Share, Delete). US1–3, US5. |
| `/documents/shared` | `Pages/SharedDocuments.razor` | Employee | **Shared with Me** (direct + team) with Preview/Download only. US6. |
| `/documents/reports` | `Pages/DocumentReports.razor` | Administrator | Uploads by type, top uploaders, access patterns (date range), activity log. US8. |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` | Employee | New task detail page (existing TODO in `Tasks.razor` navigates here) with `TaskDocuments`. US7. |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (modified) | Employee | Adds a Documents tab/section using `ProjectDocuments`. US4. |
| `/` | `Pages/Index.razor` (modified) | existing | Adds **Recent Documents** widget and Document count summary card. US7. |

`Shared/NavMenu.razor` gains "My Documents" (`bi-file-earmark-text`) and "Shared with Me" (`bi-share`) links for all signed-in users, and "Document Reports" (`bi-bar-chart`) shown only to Administrators (UI convenience only; the page and service enforce authorization).

## Reusable components (`Shared/`)

| Component | Parameters | Notes |
|---|---|---|
| `DocumentUploadForm.razor` | `int? ProjectId`, `int? TaskId`, `EventCallback OnUploaded` | `InputFile multiple`; per-file editable Title (pre-filled from file name sans extension); one shared Category (required), Description, Project (only user's projects), Tags; per-file progress bar and success/error result; form resets after completion; text explains where files appear, who can see them, and that deletion is permanent (FR-046). Max 3 clicks: Upload button → choose files → Submit (FR-012). |
| `DocumentList.razor` | `DocumentQuery`, `PagedResult<DocumentListItem>`, `bool ShowOwnerActions`, callbacks | Shared table with sort headers, pager, empty-state slot; Preview shown only for pdf/jpg/jpeg/png, otherwise "Preview unavailable"; Download is a plain link to `/documents/{id}/download`. |
| `DocumentEditModal.razor` | `int DocumentId` | Title, description, category, tags (and Replace file when `CanReplace`). |
| `DocumentShareModal.razor` | `int DocumentId` | Pick users and/or a team; list/remove current shares (owner only). |
| `ConfirmDeleteModal.razor` | title, message | Explicit confirmation stating deletion is permanent (FR-029/046). |
| `ProjectDocuments.razor` | `int ProjectId` | Used by `ProjectDetails`; upload button for members/manager. |
| `TaskDocuments.razor` | `int TaskId` | Lists task documents, attach existing (dropdown from `GetAttachableDocumentsAsync`), upload new, detach; shows refusal message when project mismatch. |
| `RecentDocumentsWidget.razor` | none | Last 5 uploads, newest first, empty state. |

## Behavioral contract

- Components obtain the user id from `AuthenticationState` `NameIdentifier` (same pattern as existing pages) and pass it to services; capability flags from `DocumentDetails` only drive which buttons render — services re-check on every action.
- All user-supplied text is rendered with Blazor's default encoding (no `MarkupString`).
- Validation messages: size limit "25 MB", accepted types "PDF, Word, Excel, PowerPoint, text, JPEG, PNG", malware rejection, project-not-permitted, attach-project-mismatch.
