---
description: "Task list for Organization Onboarding & Tenant Context (001)"
---

# Tasks: Organization Onboarding & Tenant Context

**Input**: Design documents from `/specs/001-org-tenant-context/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml)

**Tests**: **REQUIRED** — Constitution Principle IX mandates integration tests
against the public contract plus first-class cross-tenant attack tests. Test
tasks are written before their implementation and must fail first.

**Organization**: Grouped by user story. Story priority from spec.md: US1 (P1),
US2 (P1), US4 (P1), US3 (P2). US4 (isolation) is ordered before US3 by priority.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 / US4 — maps to spec.md user stories

## Path Conventions

Web-app layout per plan.md: `src/SprintSync.Api/`, `src/SprintSync.AppHost/`,
`web/sprint-sync-web/`, `tests/SprintSync.Api.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution, projects, and Aspire topology scaffolding.

- [ ] T001 Create `SprintSync.sln` and the `src/`, `web/`, `tests/` folder structure per plan.md
- [ ] T002 Create Aspire AppHost `src/SprintSync.AppHost/` declaring exactly three resources — Azure SQL database, the API project, and the React npm app (topology source of truth; no Redis/broker)
- [ ] T003 [P] Create `src/SprintSync.ServiceDefaults/` (Aspire telemetry/health/resilience defaults)
- [ ] T004 Create ASP.NET Core Minimal API project `src/SprintSync.Api/` (.NET 10) wired to ServiceDefaults
- [ ] T005 [P] Scaffold React + TypeScript + Vite app in `web/sprint-sync-web/`
- [ ] T006 [P] Create xUnit test project `tests/SprintSync.Api.Tests/` referencing `SprintSync.Api`
- [ ] T007 [P] Configure formatting/analyzers (`.editorconfig`, `dotnet format`, ESLint/Prettier for web)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The multi-tenant spine every story depends on — data, auth, tenant
context, policy, contract conventions, and the test harness.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T008 [P] Define entities `User`, `Organization`, `Membership`, `OrgRole` enum, and `TenantScopedEntity` base in `src/SprintSync.Api/Data/Entities/` per data-model.md
- [ ] T009 Implement `AppDbContext` in `src/SprintSync.Api/Data/AppDbContext.cs` — DbSets, relationships, unique indexes (`User.ExternalId`, `Membership(UserId,OrganizationId)`), and the global query filter on `TenantScopedEntity` keyed to `ITenantContext` (research R2)
- [ ] T010 Register the DbContext with `AddDbContext` (scoped, **NOT** pooled) and the Azure SQL provider; bind the connection from Aspire (research R2)
- [ ] T011 Create the initial EF Core migration (`InitialCreate`) in `src/SprintSync.Api/Data/Migrations/` and apply migrations on startup
- [ ] T012 [P] Define `ITenantContext` + scoped implementation in `src/SprintSync.Api/Tenancy/TenantContext.cs`
- [ ] T013 Implement user-resolution + JIT provisioning middleware in `src/SprintSync.Api/Auth/UserProvisioningMiddleware.cs` (resolve `User` by `oid` claim; create if absent — FR-013)
- [ ] T014 Implement tenant-resolution middleware in `src/SprintSync.Api/Tenancy/TenantResolutionMiddleware.cs` — resolve requested org from `X-Organization-Id`/persisted active org, **verify Membership**, set `ITenantContext`; this is the **only** `IgnoreQueryFilters` path (research R1, R3; Principle VI)
- [ ] T015 [P] Configure Entra External ID JWT bearer auth (Microsoft.Identity.Web) in `src/SprintSync.Api/Auth/AuthenticationSetup.cs` (Principle VII)
- [ ] T016 [P] Implement the `OrgMember` authorization requirement + handler + policy in `src/SprintSync.Api/Auth/OrgMemberPolicy.cs` (Principle VIII; no inline role strings)
- [ ] T017 [P] Configure `ProblemDetails` (RFC 9457) + global exception handler in `src/SprintSync.Api/ProblemDetailsSetup.cs` (Principle III)
- [ ] T018 [P] Configure API versioning (Asp.Versioning) under `/api/v1` and OpenAPI (Microsoft.AspNetCore.OpenApi) served at `/openapi/v1.json` (Principles II, III)
- [ ] T019 [P] Define the shared pagination envelope `PagedResult<T>` + paging-parameter binding in `src/SprintSync.Api/Contracts/PagedResult.cs` (Principle III)
- [ ] T020 Build the integration-test harness in `tests/SprintSync.Api.Tests/Infrastructure/` — `WebApplicationFactory`, a test authentication handler injecting a configurable external id, and a Testcontainers SQL Server fixture that applies real migrations (research R9)

**Checkpoint**: Spine ready — user stories can now proceed.

---

## Phase 3: User Story 1 - Create an organization and become its Owner (Priority: P1) 🎯 MVP

**Goal**: A signed-in user creates an org, becomes its Owner, and sees it as
active.

**Independent Test**: Sign in as a new user, `POST /organizations {"name":"Acme"}`,
confirm 201 with `role: Owner` and that `GET /me` shows it as the active org.

### Tests (write first, must fail)

- [ ] T021 [P] [US1] Integration test: `POST /api/v1/organizations` creates org + Owner membership, appears in list; whitespace name → 400 — in `tests/SprintSync.Api.Tests/Organizations/CreateOrganizationTests.cs`
- [ ] T022 [P] [US1] Integration test: `GET /api/v1/me` JIT-provisions a new user and returns `activeOrganizationId: null` (empty state) — in `tests/SprintSync.Api.Tests/Me/MeEndpointTests.cs`

### Implementation

- [ ] T023 [P] [US1] `CreateOrganizationRequest` + `OrganizationSummary` DTOs in `src/SprintSync.Api/Contracts/`
- [ ] T024 [P] [US1] `MeResponse` DTO in `src/SprintSync.Api/Contracts/MeResponse.cs`
- [ ] T025 [US1] Implement `POST /api/v1/organizations` in `src/SprintSync.Api/Features/Organizations/CreateOrganization.cs` — single transaction inserts `Organization` (CreatedByUserId = caller) + `Membership(caller, org, Owner)`; sets active org if caller had none (FR-001/002/003, FR-014)
- [ ] T026 [US1] Implement `GET /api/v1/me` in `src/SprintSync.Api/Features/Me/GetMe.cs`
- [ ] T027 [US1] Add name validation (trim, non-empty, ≤100) returning 400 `ProblemDetails` (FR-012)

**Checkpoint**: US1 fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - See the organizations I belong to (Priority: P1)

**Goal**: A user lists exactly the organizations they are a member of.

**Independent Test**: Seed a user into two orgs and a third they don't belong to;
`GET /organizations` returns exactly the two.

### Tests (write first, must fail)

- [ ] T028 [P] [US2] Integration test: `GET /api/v1/organizations` returns exactly the caller's orgs, excludes non-member orgs, empty list for a new user, correct pagination envelope — in `tests/SprintSync.Api.Tests/Organizations/ListOrganizationsTests.cs` (SC-003)

### Implementation

- [ ] T029 [US2] Implement `GET /api/v1/organizations` (paged, membership-scoped — the sanctioned cross-org read) in `src/SprintSync.Api/Features/Organizations/ListOrganizations.cs` (FR-005, Principle V carve-out)

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 4 - Never see or act on an organization I do not belong to (Priority: P1)

**Goal**: Isolation is structural — non-member access is indistinguishable from
"not found", and the persistence-layer filter denies cross-org rows.

**Independent Test**: As a member of "Acme" but not "Gamma", every path to
"Gamma" returns an identical 404; no "Gamma" data ever appears.

### Tests (write first, must fail)

- [ ] T030 [P] [US4] Cross-tenant attack integration tests in `tests/SprintSync.Api.Tests/Isolation/CrossTenantAccessTests.cs` — non-member `GET /organizations/{id}` → 404 indistinguishable from an unknown id; `PUT /me/active-organization` to a non-member org → 404 (unchanged); `X-Organization-Id` spoof ignored (context from verified membership); stale membership → next access denied (FR-007/008/009/010, SC-002)
- [ ] T031 [P] [US4] Query-filter mechanism proof in `tests/SprintSync.Api.Tests/Isolation/QueryFilterTests.cs` — a throwaway `TenantScopedEntity` returns only ambient-org rows and never cross-org rows (research R2/R9)

### Implementation

- [ ] T032 [P] [US4] `OrganizationDetail` DTO in `src/SprintSync.Api/Contracts/OrganizationDetail.cs`
- [ ] T033 [US4] Implement `GET /api/v1/organizations/{organizationId}` in `src/SprintSync.Api/Features/Organizations/GetOrganization.cs` — membership-gated via the `OrgMember` policy; non-member/unknown both return the identical 404 (FR-008/009, hide-existence)
- [ ] T034 [US4] Ensure not-found vs non-member responses are identical in status, `ProblemDetails` body, and branch shape (no existence/timing signal — research R4)

**Checkpoint**: Isolation guarantee proven; US1 + US2 + US4 all pass.

---

## Phase 6: User Story 3 - Choose and switch the active organization (Priority: P2)

**Goal**: A user sets and switches their active organization; the choice is
persisted and re-verified.

**Independent Test**: As a member of "Acme" and "Beta", set active to "Acme",
switch to "Beta", confirm each switch takes effect on the next request.

### Tests (write first, must fail)

- [ ] T035 [P] [US3] Integration test: `PUT /api/v1/me/active-organization` switches active org and persists it; non-member target → 404 with previous selection unchanged — in `tests/SprintSync.Api.Tests/Me/ActiveOrganizationTests.cs` (FR-006/007)

### Implementation

- [ ] T036 [P] [US3] `SetActiveOrganizationRequest` DTO in `src/SprintSync.Api/Contracts/SetActiveOrganizationRequest.cs`
- [ ] T037 [US3] Implement `PUT /api/v1/me/active-organization` in `src/SprintSync.Api/Features/Me/SetActiveOrganization.cs` — verify membership, set `User.ActiveOrganizationId`, 404 hide-existence on non-member (FR-006/007/014)

**Checkpoint**: All API user stories independently functional.

---

## Phase 7: Web UI (React) — consumes the public API

**Purpose**: The user-facing surface for the stories above. Additive over the
API MVP; the API remains the independently testable increment.

- [ ] T038 [P] Configure MSAL sign-in against Entra External ID in `web/sprint-sync-web/src/auth/`
- [ ] T039 [P] Typed API client (generated from `contracts/openapi.yaml`) attaching the bearer token in `web/sprint-sync-web/src/api/`
- [ ] T040 [US1] Create-organization form + "create your first organization" empty state in `web/sprint-sync-web/src/features/organizations/`
- [ ] T041 [US2] Organization list view in `web/sprint-sync-web/src/features/organizations/`
- [ ] T042 [US3] Active-organization switcher in `web/sprint-sync-web/src/features/organizations/`

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Deployment cost controls, contract verification, demo data.

- [ ] T043 [P] Configure ACA scale-to-zero (`minReplicas = 0`) and Azure SQL free serverless + auto-pause via `ConfigureInfrastructure` in `src/SprintSync.AppHost/` (Principle 12, research R8)
- [ ] T044 [P] Configure React deployment to Azure Static Web Apps Free + `azd` wiring
- [ ] T045 Pre-deploy guard: document/automate the `azd infra synth` review (no `minReplicas > 0`, no idle-billable resources) in `quickstart.md`/CI (Principle 12)
- [ ] T046 [P] Verify `/openapi/v1.json` is published and matches `contracts/openapi.yaml` (Principle III)
- [ ] T047 [P] Implement a deterministic demo seeder (users, orgs, memberships) for local runs in `src/SprintSync.Api/Data/DemoSeeder.cs`
- [ ] T048 Run `quickstart.md` validation end-to-end (all journeys + isolation attacks)
- [ ] T049 [P] Add a $1 Azure budget alert as the cost backstop (Principle 12)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **blocks all user stories**.
- **User Stories (Phases 3–6)**: all depend on Foundational. By priority: US1 → US2 → US4 → US3. US2/US4/US3 are independently testable and could be parallelized across developers once Foundational is done.
- **Web UI (Phase 7)**: depends on the corresponding API endpoints existing (T040←US1, T041←US2, T042←US3).
- **Polish (Phase 8)**: after the desired stories are complete.

### Critical path within Foundational

T008 → T009 → T010 → T011 (data + context + migrations) gate everything.
T013/T014 (provisioning + tenant resolution) gate every authenticated endpoint.
T020 (test harness) gates every test task.

### Within each user story

Tests (must fail first) → DTOs → endpoint → validation. Endpoints in different
files marked [P] within a story can be built in parallel.

---

## Parallel Opportunities

- Setup: T003, T005, T006, T007 in parallel.
- Foundational: T012, T015, T016, T017, T018, T019 in parallel after T008–T011; T013/T014 are sequential on the DbContext.
- US4 tests T030 and T031 in parallel; US-level DTO tasks (T023/T024, T032, T036) in parallel with their story's test tasks.
- Polish: T043, T044, T046, T047, T049 in parallel.

## Parallel Example: User Story 1

```bash
# Tests first (parallel):
Task: "Integration test POST /organizations in tests/.../Organizations/CreateOrganizationTests.cs"
Task: "Integration test GET /me in tests/.../Me/MeEndpointTests.cs"

# Then DTOs (parallel):
Task: "CreateOrganizationRequest + OrganizationSummary DTOs in src/SprintSync.Api/Contracts/"
Task: "MeResponse DTO in src/SprintSync.Api/Contracts/MeResponse.cs"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (the bulk — the multi-tenant spine) →
3. Phase 3 US1 → **STOP & validate**: create an org, become Owner, see it active.
Demo-able.

### Incremental Delivery

Foundational → US1 (create) → US2 (list) → US4 (isolation, the safety proof) →
US3 (switch) → Web UI → Polish/deploy. Each API story is a tested increment that
doesn't break the previous ones.

### Constitution-critical ordering

US4's cross-tenant attack suite (T030) and the query-filter proof (T031) are
**not** deferrable polish — they are the Principle IX evidence that the isolation
spine actually holds, and they gate any claim that the feature is "done."

---

## Notes

- [P] = different files, no dependencies. [Story] label maps to spec.md user story.
- Tests are required (Principle IX) and written to fail before implementation.
- Commit after each task or logical group; keep `main` clean via the feature branch.
- The bulk of the work is Foundational — expected for a walking-skeleton slice
  that stands up the entire multi-tenant spine once.
