# Quickstart: Validating Document Upload and Management

**Feature**: `001-document-management` | A run/validation guide. Behavior details live in [spec.md](spec.md), [data-model.md](data-model.md), and [contracts/](contracts/).

## Prerequisites

- .NET SDK matching `net10.0`, SQL Server LocalDB (`(localdb)\mssqllocaldb`), a desktop browser. No internet or cloud account is needed.
- Seed users (mock login on `/login`): `admin@contoso.com` (Administrator), `camille.nicole@contoso.com` (Project Manager of project 1), `floris.kregel@contoso.com` (Team Lead, Engineering, project member), `ni.kang@contoso.com` (Employee, Engineering, project member).

## Setup

From the repo root:

```powershell
# One time after pulling this feature: EnsureCreated() will not add tables to an existing DB
dotnet ef database drop --force --project ContosoDashboard
dotnet build ContosoDashboard
dotnet run --project ContosoDashboard     # URL printed in console / Properties/launchSettings.json
```

Expected: app starts, creates and seeds the database (including the new `Documents`, `DocumentTags`, `DocumentShares`, `DocumentActivities`, `TaskDocuments` tables), no new build warnings. Uploaded files appear under `ContosoDashboard/AppData/uploads/` (outside `wwwroot`) with GUID names.

## Automated tests

```powershell
dotnet test ContosoDashboard.Tests
```

Expected: all pass. Coverage must include success **and** unauthorized paths for each service method (Principle V) plus validation, no-orphan, share-removal, team-membership, task-attach, and activity-retention cases.

## Validation scenarios (manual, end to end)

Use two browsers/profiles to be signed in as two users at once.

| # | Scenario | Steps | Expected | Spec |
|---|---|---|---|---|
| 1 | Upload happy path | Ni Kang → My Documents → Upload → choose a small PDF → category "Reports" → Submit | Progress shown, success message; row shows title, category, date, size, project; file on disk has a GUID name | US1, FR-006/009 |
| 2 | Batch upload | Choose 3 files incl. one `.exe` | Titles pre-filled/editable per file; valid files succeed, `.exe` rejected with accepted-types list | US1.2/1.6, FR-007 |
| 3 | Missing required fields | Clear title / skip category | Submit blocked, fields flagged | US1.4 |
| 4 | Size / empty / disguised | 26 MB file; 0-byte file; `evil.exe` renamed `evil.pdf` | Each rejected with clear message; nothing stored (check `AppData/uploads`) | FR-003/004, SC-011 |
| 5 | Malware stub | Upload a text file containing the EICAR test string | Rejected as malware; nothing stored or listed | FR-005 |
| 6 | Project association | Ni Kang uploads with project "ContosoDashboard Development"; project picker lists only own projects | Appears in project's documents for Camille and Floris; admin sees it | US4, FR-010/022 |
| 7 | Browse/sort/filter/search | Several docs of different categories/projects | Sorting by title/date/category/size works; filters combine; search by title, description, tag, uploader, project | US2 |
| 8 | Preview/download | Preview PDF and PNG (opens new tab), download both; Office/text show "Preview unavailable" | Content intact, original name on download | US3 |
| 9 | IDOR | As an unrelated user (or after removing membership) browse to `/documents/{id}/download` and `/preview` for another user's Personal Files doc, and for a non-existent id | Identical `404` for both; document absent from list/search results | US3.4, FR-016, SC-005 |
| 10 | Edit / replace / delete | Owner edits metadata, replaces file, deletes (confirm + cancel paths); Camille deletes Ni's project doc; Floris edits a same-department member's doc; Floris cannot replace or share it | Matches the access matrix in data-model.md; delete removes file from disk | US5, FR-027–030 |
| 11 | Sharing | Ni shares with Floris and with team "Engineering"; recipients see notification and "Shared with Me"; Ni removes share; recipient retries link | Immediate loss of access; non-owner share attempt refused | US6, FR-031–035 |
| 12 | Task integration | Open a task → upload doc / attach existing project-less doc; try attaching a doc from another project | Doc linked and adopts task's project; mismatch refused with message | US7, FR-036/037 |
| 13 | Dashboard | Home page after uploads/deletes | Recent Documents (5, newest first) and Document count update; empty state at zero | US7, FR-038/039 |
| 14 | Activity & reports | Perform upload, download, preview, share, unshare, delete; sign in as admin → Document Reports; try the URL as Employee | All actions logged (including delete of removed doc); 3 reports render; non-admin denied | US8, FR-040–042 |
| 15 | Fresh-bootstrap | Drop DB, delete `AppData/uploads`, run app | Starts and seeds cleanly with no documents | Principle V |
| 16 | Existing README security scenarios | Run authentication-required, user-isolation, IDOR and role-based scenarios from README | Still pass | Principle V |

## Performance spot checks

- Upload a 25 MB file: completes ≤ 30 s (FR-043). Seed ~500 documents (test helper or script): My Documents and search respond ≤ 2 s (FR-044); PDF preview appears ≤ 3 s (FR-045).

## Done criteria

All automated tests pass, scenarios 1–16 behave as expected, build has no new warnings, and README (features, routes, structure, Known Limitations: stub malware scanner, local file storage) is updated.
