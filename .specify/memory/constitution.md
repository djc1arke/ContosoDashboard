<!--
Sync Impact Report (temporary; remove before commit)
- Version change: (unversioned template) -> 1.0.0
- Modified principles: none renamed (initial adoption; all placeholders replaced)
- Added sections: Core Principles I-VI, Technical Constraints & Training Scope,
  Development Workflow & Quality Gates, Governance
- Removed sections: none
- Templates reviewed: .specify/templates/plan-template.md (Constitution Check gate reads this
  file at runtime), spec-template.md, tasks-template.md, checklist-template.md - no edits needed
- Follow-up TODOs:
  - README states "ASP.NET Core 8.0" but ContosoDashboard.csproj targets net10.0 (packages are
    8.0.0). Constitution records the csproj as the source of truth; reconcile README separately.
  - No automated test project exists yet; Principle V defines the testing expectation for new work.
-->
# ContosoDashboard Constitution

## Core Principles

### I. Training-Purpose, Offline-First (NON-NEGOTIABLE)
ContosoDashboard is a training application for Spec-Driven Development. It MUST run fully offline
with no cloud subscriptions or external service dependencies (SQL Server LocalDB, local filesystem,
mock cookie authentication). Features MUST NOT introduce a hard dependency on any external or cloud
service. Documented training limitations (mock auth, no rate limiting, no audit logging) are
intentional; they MUST NOT be presented as production-ready, and new features MUST NOT silently
widen them.
Rationale: students must be able to complete the training without accounts, cost, or connectivity.

### II. Infrastructure Behind Interfaces
Every infrastructure dependency (file storage, identity, data access beyond EF Core, notifications
delivery) MUST be consumed through an interface registered via dependency injection, with the local
implementation as the default. Business logic and UI MUST NOT reference concrete infrastructure
types. New file-based features MUST use `IFileStorageService` (`UploadAsync`, `DeleteAsync`,
`DownloadAsync`, `GetUrlAsync`) with a `LocalFileStorageService` implementation, so an Azure Blob
implementation can be swapped in without changing business logic, UI, or schema.
Rationale: demonstrates the cloud-migration path described in the README without requiring it.

### III. Layered Architecture & Service Contracts
Code MUST follow the existing separation: `Models/` (entities), `Data/` (`ApplicationDbContext`),
`Services/` (business logic behind `I<Name>Service` interfaces, registered as scoped), and
`Pages/`/`Shared/` (Blazor Server UI and Razor Pages for login/logout). Razor components MUST call
services and MUST NOT query `ApplicationDbContext` directly. Data access MUST be async, use
`.Include()` eager loading to avoid N+1 queries, and add indexes for frequently queried fields.
Nullable reference types MUST remain enabled and warnings MUST NOT be suppressed to hide defects.

### IV. Defense-in-Depth Security (NON-NEGOTIABLE)
Authorization MUST be enforced at three layers: middleware/policies, `[Authorize]` on every
protected page, and service-level checks that scope data to the requesting user (IDOR protection).
Every new service method that reads or mutates user-owned data MUST take the requesting user's
identity and verify access (owner, project member/manager, or role) before returning or changing
data. The four hierarchical roles (Employee, TeamLead, ProjectManager, Administrator) and their
policies MUST be reused, not reinvented. The security headers and 8-hour sliding cookie in
`Program.cs` MUST NOT be weakened. File uploads MUST validate extension against an allow-list and
size limits, MUST store files outside `wwwroot` under GUID-based paths, MUST NEVER use user-supplied
filenames in paths, and MUST be served only through authorization-checked endpoints.
Rationale: IDOR protection and layered authorization are the core security lessons of this project.

### V. Verifiable Increments & Test Expectations
Each feature MUST be specified (spec.md) with independently testable user stories and acceptance
scenarios before implementation. New services and authorization rules MUST have automated tests
covering the success path and at least the unauthorized-access path (e.g., a user accessing another
user's or non-member project's data). Features that change persisted data MUST keep the seed data
and `EnsureCreated()` bootstrap working on a fresh LocalDB. Manual security scenarios in the README
(authentication required, user isolation, IDOR, role-based features) MUST continue to pass.
Rationale: no test project exists yet; this principle sets the bar for all new work so regressions
in security behavior are caught automatically.

### VI. Simplicity & Consistency
Changes MUST follow existing conventions (naming, async/await everywhere, Bootstrap 5.3 with
Bootstrap Icons, dependency injection for all services). Prefer the smallest change that meets the
spec; new projects, frameworks, or abstractions MUST be justified in the plan's complexity tracking.
Upload sequence MUST be: generate unique path -> save file -> save metadata, to avoid orphaned or
duplicate-key records.

## Technical Constraints & Training Scope

- **Platform**: ASP.NET Core Blazor Server, C# with nullable enabled, target framework as declared
  in `ContosoDashboard.csproj` (currently `net10.0`; the csproj is the source of truth).
- **Data**: SQL Server LocalDB via Entity Framework Core (`EnsureCreated()` for development).
  Schema changes MUST update seed data and the model/`ApplicationDbContext` configuration together.
- **Authentication**: Cookie-based mock authentication with claims-based identity; real identity
  providers are out of scope but MUST remain swappable (Microsoft Entra ID migration path).
- **UI**: Bootstrap 5.3 and Bootstrap Icons; Content-Security-Policy changes MUST be justified and
  documented in the README's Known Limitations.
- **Out of scope**: production hardening, cloud deployment, real virus-scanning services. Where a
  stakeholder requirement implies one (e.g., malware scanning), it MUST be implemented behind an
  interface with an offline stub.
- **Secrets**: No credentials, keys, or real personal data may be committed to the repository.

## Development Workflow & Quality Gates

- Work follows the Spec Kit flow: constitution -> specify -> clarify -> plan -> tasks -> implement.
  Stakeholder documents in `StakeholderDocs/` are inputs to specs, not substitutes for them.
- Every plan MUST pass the Constitution Check against Principles I-VI before design and again after
  design; violations MUST be recorded and justified in the plan.
- A change is mergeable only when it builds without new warnings, the application starts and seeds
  on a fresh LocalDB, applicable tests pass, and the reviewer has confirmed authorization checks at
  all three layers for any new page, endpoint, or service method.
- README MUST be updated when features, routes, structure, roles, or known limitations change.

## Governance

This constitution supersedes other project practices and guidance. Amendments MUST be made through
the `/speckit.constitution` workflow, documented with a Sync Impact Report, and accompanied by any
required follow-up changes to specs, plans, or code. Versioning follows semantic versioning:
MAJOR for backward-incompatible removals or redefinitions of principles, MINOR for added principles
or materially expanded guidance, PATCH for clarifications and wording fixes. All plans, task lists,
and code reviews MUST verify compliance with this constitution; unavoidable deviations MUST be
justified in writing in the plan's complexity tracking. Compliance is reviewed at each feature plan
and pull request. Runtime development guidance lives in `README.md`.

**Version**: 1.0.0 | **Ratified**: 2026-10-22 | **Last Amended**: 2026-10-22
