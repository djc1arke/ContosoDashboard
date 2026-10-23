# Contract: HTTP Endpoints

**Feature**: `001-document-management`

The feature exposes exactly two HTTP endpoints (file delivery). Everything else (upload, list, edit, share, reports) is Blazor Server UI calling the service interfaces in [service-interfaces.md](service-interfaces.md); there is no public JSON API.

Both endpoints: registered via minimal APIs in `Program.cs`, `RequireAuthorization("Employee")` (layer 1), user id from the `NameIdentifier` claim, then `IDocumentService.OpenReadAsync` (layer 3 — service-level authorization and activity logging). Route parameter is the integer `DocumentId`.

## GET `/documents/{id:int}/download`

| Aspect | Contract |
|---|---|
| Auth | Cookie auth required. Unauthenticated → existing cookie challenge (redirect to `/login`). |
| Success | `200` streamed file. `Content-Type` = server-derived type for the stored `FileType`; `Content-Disposition: attachment; filename="<sanitized OriginalFileName>"; filename*=UTF-8''<encoded>`; `Content-Length` = stored size; `X-Content-Type-Options: nosniff`; `Cache-Control: private, no-store`. |
| Not found / not authorized | `404 Not Found`, empty body, **identical** for both cases (FR-016). |
| Missing stored file (record exists, file missing) | `404` to caller (no path details); error logged server-side at `Error` level (spec edge case: not silently ignored). |
| Side effect | One `DocumentActivity` row, `ActionType = Download`, written for every authorized request. |

## GET `/documents/{id:int}/preview`

| Aspect | Contract |
|---|---|
| Auth | Same as download. |
| Success | `200` streamed file inline, only when `FileType ∈ {pdf, jpg, jpeg, png}`: `Content-Disposition: inline; filename="..."`, same safe headers as download. |
| Non-previewable type | `404` (the UI never offers Preview for these; direct requests are refused identically). |
| Not found / not authorized / missing file | `404` as above. |
| Side effect | One `DocumentActivity` row, `ActionType = Preview`, distinct from Download. |
| Framing | Opened in a new tab or used as `<img src>`. Not framed (global `X-Frame-Options: DENY` remains unchanged). |

## Non-goals / Security notes

- No endpoint serves files by storage path or GUID; the only addressing is `DocumentId` plus the authorization check.
- No write endpoints exist; uploads run through the Blazor circuit (`InputFile`) and `IDocumentService`.
- No CORS, no anonymous access, no range-request support required (full-file responses).
