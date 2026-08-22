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

- [x] T001 Create `SprintSync.sln` and the `src/`, `web/`, `tests/` folder structure per plan.md
- [x] T002 Create Aspire AppHost `src/SprintSync.AppHost/` declaring exactly three resources — Azure SQL database, the API project, and the React npm app (topology source of truth; no Redis/broker)
- [x] T003 [P] Create `src/SprintSync.ServiceDefaults/` (Aspire telemetry/health/resilience defaults)
- [x] T004 Create ASP.NET Core Minimal API project `src/SprintSync.Api/` (.NET 10) wired to ServiceDefaults
- [x] T005 [P] Scaffold React + TypeScript + Vite app in `web/sprint-sync-web/`
- [x] T006 [P] Create xUnit test project `tests/SprintSync.Api.Tests/` referencing `SprintSync.Api`
- [x] T007 [P] Configure formatting/analyzers (`.editorconfig`, `dotnet format`, ESLint/Prettier for web)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The multi-tenant spine every story depends on — data, auth, tenant
context, policy, contract conventions, and the test harness.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T008 [P] Define entities `User`, `Organization`, `Membership`, `OrgRole` enum, and `TenantScopedEntity` base in `src/SprintSync.Api/Data/Entities/` per data-model.md
- [x] T009 Implement `AppDbContext` in `src/SprintSync.Api/Data/AppDbContext.cs` — DbSets, relationships, unique indexes (`User.ExternalId`, `Membership(UserId,OrganizationId)`), and the global query filter on `TenantScopedEntity` keyed to `ITenantContext` (research R2)
- [x] T010 Register the DbContext with `AddDbContext` (scoped, **NOT** pooled) and the Azure SQL provider; bind the connection from Aspire (research R2)
- [x] T011 Create the initial EF Core migration (`InitialCreate`) in `src/SprintSync.Api/Data/Migrations/` and apply migrations on startup
- [x] T012 [P] Define `ITenantContext` + scoped implementation in `src/SprintSync.Api/Tenancy/TenantContext.cs`
- [x] T013 Implement user-resolution + JIT provisioning middleware in `src/SprintSync.Api/Auth/UserProvisioningMiddleware.cs` (resolve `User` by `oid` claim; create if absent — FR-013)
- [x] T014 Implement tenant-resolution middleware in `src/SprintSync.Api/Tenancy/TenantResolutionMiddleware.cs` — resolve requested org from `X-Organization-Id`/persisted active org, **verify Membership**, set `ITenantContext`; if the persisted active org is stale/invalid, re-resolve to another of the user's memberships or the empty state and never use the stale value (FR-014); this is the **only** `IgnoreQueryFilters` path (research R1, R3; Principle VI)
- [x] T015 [P] Configure Entra External ID JWT bearer auth (Microsoft.Identity.Web) in `src/SprintSync.Api/Auth/AuthenticationSetup.cs` (Principle VII)
- [x] T016 [P] Implement the `OrgMember` authorization requirement + handler + policy in `src/SprintSync.Api/Auth/OrgMemberPolicy.cs` (Principle VIII; no inline role strings)
- [x] T017 [P] Configure `ProblemDetails` (RFC 9457) + global exception handler in `src/SprintSync.Api/ProblemDetailsSetup.cs` (Principle III)
- [x] T018 [P] Configure API versioning (Asp.Versioning) under `/api/v1` and OpenAPI (Microsoft.AspNetCore.OpenApi) served at `/openapi/v1.json` (Principles II, III)
- [x] T019 [P] Define the shared pagination envelope `PagedResult<T>` + paging-parameter binding in `src/SprintSync.Api/Contracts/PagedResult.cs` (Principle III)
- [x] T020 Build the integration-test harness in `tests/SprintSync.Api.Tests/Infrastructure/` — `WebApplicationFactory`, a test authentication handler injecting a configurable external id, and a Testcontainers SQL Server fixture that applies real migrations (research R9)

**Checkpoint**: Spine ready — user stories can now proceed.

---

## Phase 3: User Story 1 - Create an organization and become its Owner (Priority: P1) 🎯 MVP

**Goal**: A signed-in user creates an org, becomes its Owner, and sees it as
active.

**Independent Test**: Sign in as a new user, `POST /organizations {"name":"Acme"}`,
confirm 201 with `role: Owner` and that `GET /me` shows it as the active org.

### Tests (write first, must fail)

- [x] T021 [P] [US1] Integration test: `POST /api/v1/organizations` creates org + Owner membership, appears in list; whitespace name → 400 — in `tests/SprintSync.Api.Tests/Organizations/CreateOrganizationTests.cs`
- [x] T022 [P] [US1] Integration test: `GET /api/v1/me` JIT-provisions a new user and returns `activeOrganizationId: null` (empty state) — in `tests/SprintSync.Api.Tests/Me/MeEndpointTests.cs`

### Implementation

- [x] T023 [P] [US1] `CreateOrganizationRequest` + `OrganizationSummary` DTOs in `src/SprintSync.Api/Contracts/`
- [x] T024 [P] [US1] `MeResponse` DTO in `src/SprintSync.Api/Contracts/MeResponse.cs`
- [x] T025 [US1] Implement `POST /api/v1/organizations` in `src/SprintSync.Api/Features/Organizations/CreateOrganization.cs` — single transaction inserts `Organization` (CreatedByUserId = caller) + `Membership(caller, org, Owner)`; sets active org if caller had none (FR-001/002/003, FR-014)
- [x] T026 [US1] Implement `GET /api/v1/me` in `src/SprintSync.Api/Features/Me/GetMe.cs` — returns the resolved active org, applying the stale-active-org fallback (FR-014)
- [x] T027 [US1] Add name validation (trim, non-empty, ≤100) returning 400 `ProblemDetails` (FR-012)

**Checkpoint**: US1 fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - See the organizations I belong to (Priority: P1)

**Goal**: A user lists exactly the organizations they are a member of.

**Independent Test**: Seed a user into two orgs and a third they don't belong to;
`GET /organizations` returns exactly the two.

### Tests (write first, must fail)

- [x] T028 [P] [US2] Integration test: `GET /api/v1/organizations` returns exactly the caller's orgs, excludes non-member orgs, empty list for a new user, correct pagination envelope — in `tests/SprintSync.Api.Tests/Organizations/ListOrganizationsTests.cs` (SC-003)

### Implementation

- [x] T029 [US2] Implement `GET /api/v1/organizations` (paged, membership-scoped — the sanctioned cross-org read) in `src/SprintSync.Api/Features/Organizations/ListOrganizations.cs` (FR-005, Principle V carve-out)

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 4 - Never see or act on an organization I do not belong to (Priority: P1)

**Goal**: Isolation is structural — non-member access is indistinguishable from
"not found", and the persistence-layer filter denies cross-org rows.

**Independent Test**: As a member of "Acme" but not "Gamma", every path to
"Gamma" returns an identical 404; no "Gamma" data ever appears.

### Tests (write first, must fail)

- [x] T030 [P] [US4] Cross-tenant attack integration tests in `tests/SprintSync.Api.Tests/Isolation/CrossTenantAccessTests.cs` — non-member `GET /organizations/{id}` → 404 indistinguishable from an unknown id; `PUT /me/active-organization` to a non-member org → 404 (unchanged); `X-Organization-Id` spoof ignored (context from verified membership); stale membership → next access denied (FR-007/008/009/010, SC-002)
- [x] T031 [P] [US4] Query-filter mechanism proof in `tests/SprintSync.Api.Tests/Isolation/QueryFilterTests.cs` — a throwaway `TenantScopedEntity` returns only ambient-org rows and never cross-org rows (research R2/R9)

### Implementation

- [x] T032 [P] [US4] `OrganizationDetail` DTO in `src/SprintSync.Api/Contracts/OrganizationDetail.cs`
- [x] T033 [US4] Implement `GET /api/v1/organizations/{organizationId}` in `src/SprintSync.Api/Features/Organizations/GetOrganization.cs` — fetch via a **single uniform membership-gated query** (no separate "does org exist?" lookup, no existence-vs-membership branch, no extra round-trip); non-member and unknown ids both return byte-identical 404 + ProblemDetails (FR-008/009, hide-existence, research R4)
- [x] T034 [US4] Enforce timing-oracle resistance by construction: (a) implement a custom authorization result handler in `src/SprintSync.Api/Auth/HideExistenceAuthorizationResultHandler.cs` mapping `OrgMember` policy denials on hide-existence resources to 404 (never 403) with the uniform ProblemDetails (Principle VIII preserved); (b) add a timing-parity regression test in `tests/SprintSync.Api.Tests/Isolation/CrossTenantAccessTests.cs` asserting non-member vs unknown-id latency stays within a tolerance band over repeated samples (FR-009, research R4)

**Checkpoint**: Isolation guarantee proven; US1 + US2 + US4 all pass.

> **Note**: T030 asserts hide-existence on `PUT /me/active-organization`, whose
> implementation is T036/T037 in Phase 6. Those two were pulled forward into
> Phase 5 so the US4 guarantee could be proven on the "act" path as well as the
> "read" path. Phase 6 therefore has only its own test (T035) left.

---

## Phase 6: User Story 3 - Choose and switch the active organization (Priority: P2)

**Goal**: A user sets and switches their active organization; the choice is
persisted and re-verified.

**Independent Test**: As a member of "Acme" and "Beta", set active to "Acme",
switch to "Beta", confirm each switch takes effect on the next request.

### Tests (write first, must fail)

- [x] T035 [P] [US3] Integration test: `PUT /api/v1/me/active-organization` switches active org and persists it; non-member target → 404 with previous selection unchanged; **and** a stale/invalid persisted active org resolves to another valid org (or null empty state) on `GET /me`, never the stale one and never granting access — in `tests/SprintSync.Api.Tests/Me/ActiveOrganizationTests.cs` (FR-006/007/014)

### Implementation

- [x] T036 [P] [US3] `SetActiveOrganizationRequest` DTO in `src/SprintSync.Api/Contracts/SetActiveOrganizationRequest.cs`
- [x] T037 [US3] Implement `PUT /api/v1/me/active-organization` in `src/SprintSync.Api/Features/Me/SetActiveOrganization.cs` — verify membership, set `User.ActiveOrganizationId`, 404 hide-existence on non-member (FR-006/007/014)

**Checkpoint**: All API user stories independently functional.

---

## Phase 7: Web UI (React) — consumes the public API

**Purpose**: The user-facing surface for the stories above. Additive over the
API MVP; the API remains the independently testable increment.

- [x] T038 [P] Configure MSAL sign-in against Entra External ID in `web/sprint-sync-web/src/auth/`
- [x] T039 [P] Typed API client (generated from `contracts/openapi.yaml`) attaching the bearer token in `web/sprint-sync-web/src/api/`
- [x] T040 [US1] Create-organization form + "create your first organization" empty state in `web/sprint-sync-web/src/features/organizations/`
- [x] T041 [US2] Organization list view in `web/sprint-sync-web/src/features/organizations/`
- [x] T042 [US3] Active-organization switcher in `web/sprint-sync-web/src/features/organizations/`

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Deployment cost controls, contract verification, demo data.

- [x] T043 [P] Configure ACA scale-to-zero (`minReplicas = 0`) and Azure SQL free serverless + auto-pause via `ConfigureInfrastructure` in `src/SprintSync.AppHost/` (Principle 12, research R8)
- [x] T044 [P] Configure React deployment to Azure Static Web Apps Free + `azd` wiring
- [x] T045 Pre-deploy guard: document/automate the `azd infra synth` review (no `minReplicas > 0`, no idle-billable resources) in `quickstart.md`/CI (Principle 12)
- [x] T046 [P] Verify `/openapi/v1.json` is published and matches `contracts/openapi.yaml` (Principle III)
- [x] T047 [P] Implement a deterministic demo seeder (users, orgs, memberships) for local runs in `src/SprintSync.Api/Data/DemoSeeder.cs`
- [x] T048 Run `quickstart.md` validation end-to-end (all journeys + isolation attacks)

> **T048 validation record**: `dotnet run --project src/SprintSync.AppHost` brings up
> all three resources (SQL container, API, Vite SPA). Verified against the running
> stack: `/openapi/v1.json` publishes `Sprint Sync API` v1.0 with exactly the four
> contracted paths; all five operations return 401 unauthenticated; the demo seeder
> produced its cast (Alice 2 orgs/active Acme, Bob 2 orgs/active Gamma, Carol empty
> state). Every user journey and isolation attack in quickstart.md is covered by the
> automated suite (31 tests), which quickstart.md itself designates as the
> authoritative check. **Not validated**: the signed-in browser walkthrough, which
> needs a live Entra External ID tenant that does not exist for this repo.
- [x] T049 [P] Add a $1 Azure budget alert as the cost backstop (Principle 12)

---

## Phase 9: Handoff Follow-Ups — Deploy Validation & Hardening

**Source**: `.specify/handoffs/2026-08-21-org-tenant-committee-followups.md`
(code-review committee, 5 rounds). Everything above shipped and is verified —
56/56 backend integration tests on real SQL, 5/5 web tests, both guard scripts.
This phase carries the work that **could not be verified in the build
environment** (no live Azure deploy, no real Entra tenant, no CI runner) plus the
refinements the committee marked optional.

**Do not re-litigate** the decisions recorded in the handoff's "Decisions already
made" section (scoped non-pooled DbContext, hide-existence 404, `X-Organization-Id`
as a membership-verified soft hint, create-only JIT provisioning, the ~$5-8/mo
idle floor).

### Phase 9A: Buildable now (no live Azure required)

- [x] T050 [P] Guard migrate-on-startup against the cross-revision rollout window in `src/SprintSync.Api/Program.cs` — wrap the `MigrateAsync` call in a SQL `sp_getapplock`/`sp_releaseapplock` pair (session or transaction scoped, acquired inside the existing execution strategy) so two revisions cannot apply DDL concurrently; replace the CAVEAT comment at Program.cs:105-110 with what the lock now guarantees. Handoff item 3 (Round 5 P1-4).
- [x] T051 [P] Thread `CancellationToken` (`HttpContext.RequestAborted`) through every EF call in `src/SprintSync.Api/Auth/UserProvisioningMiddleware.cs`, `src/SprintSync.Api/Tenancy/TenantResolutionMiddleware.cs`, `src/SprintSync.Api/Features/Me/MeEndpoints.cs`, and `src/SprintSync.Api/Features/Organizations/OrganizationEndpoints.cs` (bind the token as a Minimal API parameter in the endpoints). Handoff item 5 (Round 5 P2-8).
- [x] T052 [P] Deepen the file-vs-served OpenAPI contract test in `tests/SprintSync.Api.Tests/Contract/OpenApiContractTests.cs` — beyond the current operation-set and response-body-property assertions, assert that each contracted operation's **declared response status codes** and its **request-body required fields** match `specs/001-org-tenant-context/contracts/openapi.yaml`. Handoff item 6 (Round 5 P2-9).
- [x] T053 [P] Rewrite the tenant global query filter in `src/SprintSync.Api/Data/AppDbContext.cs:120-133` from the `Expression.Constant(this)` reflection form to the idiomatic context-instance-member reference; `tests/SprintSync.Api.Tests/Isolation/QueryFilterTests.cs` must stay green (behaviour-preserving refactor only). Handoff item 7 (Round 5 P2-7).
- [ ] T054 [P] **(OPTIONAL — reviewer: no change required; NOT taken up, see record below)** Refresh the provisioned `DisplayName` from the token on repeat login in `src/SprintSync.Api/Auth/UserProvisioningMiddleware.cs`, guarded to write only when the value actually changed. Handoff item 8 (Round 5 P2-13). Skip unless the create-only JIT decision is deliberately revisited.

> **Phase 9A validation record**: T050–T053 are complete and **verified green in
> CI** — PR #3, workflow run 32537061874, commit `21de171`:
>
> - `Build succeeded. 15 Warning(s) 0 Error(s)` (Release, .NET 10).
> - `Passed! - Failed: 0, Passed: 57, Skipped: 0, Total: 57` against real SQL
>   (Testcontainers). 57 = the 56 local baseline, less the excluded
>   `Category=Timing` test, plus the two new contract tests from T052 — so both
>   new assertions genuinely executed rather than being silently uncollected.
> - Web job green (5/5 vitest, tsc + vite build), idle-cost guard green.
>
> What that run settles, per change:
> - **T050** — the `sp_getapplock` path runs on every `WebApplicationFactory`
>   boot in the suite (`Development` ⇒ migrate-on-startup), so the lock was
>   acquired and released against real SQL Server dozens of times.
> - **T052** — the two-way status-code equality holds: the `.Produces(401)` /
>   `.ProducesValidationProblem()` declarations added to the endpoints match the
>   contract exactly, and ASP.NET Core injected no status codes of its own.
> - **T053** — `QueryFilterTests` passed, including
>   `Filter_IsReEvaluatedPerContextInstance` and
>   `Filter_SurvivesTenantChangeWithinOneContext`, so the idiomatic
>   context-instance-member lambda preserves per-instance re-evaluation. The
>   revert-first advice that stood here before CI ran is withdrawn.
>
> The code was authored in a session with no .NET SDK and no Docker (the SDK
> host is refused by egress policy), so CI was its first compile. That is a
> statement about how it was produced, not about its current status.
>
> **T054 was deliberately not taken up.** The handoff records create-only JIT
> provisioning as a settled decision and the reviewer marked the item "no change
> required"; implementing it would reopen a decision nobody asked to revisit.
>
> **Known lint gap (not introduced here, not fixed here):** the build emits
> `EnableGenerateDocumentationFile` warnings — `.editorconfig` sets IDE0005
> (unused usings) to `error`, but `Directory.Build.props` sets
> `GenerateDocumentationFile=false`, and IDE0005 does not run on build without
> it. That rule has therefore never been enforced. Worth its own task.

### Phase 9B: Deploy-gated (require a real `azd up` / live Entra tenant / CI runner)

- [ ] T055 On a real `azd up`, confirm the API container app provisions **and receives non-empty `AzureAd__*` environment values**; determine whether it deploys from `infra/main.bicep` or from the azd-applied module at `azd deploy`, then resolve `infra/api/api-containerapp.module.bicep` — which is currently unreachable from `main.bicep` (its five modules are api-identity, api-roles-sql, cae, cae-acr, sql) — by wiring it in or deleting it. **Do not hand-wire `main.bicep` blind**: it may conflict with azd's own deploy model. Handoff item 1 (Round 5 P0-1). **P0 — blocks T056.**
- [ ] T056 After T055, extend `scripts/check-idle-cost.sh` and `scripts/check-idle-cost.ps1` to reject orphan `Microsoft.App/containerApps` modules that are not reachable from `infra/main.bicep`, so mere presence of a `.bicep` file under `infra/` can no longer satisfy the fail-closed `saw_containerapp`/`saw_zero_floor` checks. Keep both twins behaviourally identical. Handoff item 2 (Round 5 P1-3). **Constitution basis:** Deployment & Cost Constraints → "Pre-deploy verification" (v1.1.0) now defines the reviewed set as the resources reachable from `main.bicep`; this task makes the guard enforce that.
- [ ] T057 Add ACA liveness/readiness probes targeting the already-exposed `/alive` (and `/health`) endpoints via `ConfigureInfrastructure` in `src/SprintSync.AppHost/`, and enable the stubbed Azure Monitor OTLP exporter in `src/SprintSync.ServiceDefaults/Extensions.cs:91-94` behind `APPLICATIONINSIGHTS_CONNECTION_STRING`; confirm traces actually arrive. **Verify against a real deploy** — a wrong probe port crash-loops the container. Handoff item 4 (Rounds 2 & 5). **Constitution basis:** Principle X (Operable by Default, added v1.1.0) — probes must be wired by the platform and OTLP export enabled whenever the environment supplies a destination.
- [ ] T058 Run `.github/workflows/ci.yml` live on a real runner and perform a real `azd up`, then validate end-to-end: the cross-tenant attack suite (`tests/SprintSync.Api.Tests/Isolation/`) against the deployed API and an interactive Entra External ID sign-in through the web app. Record the outcome under the T048 validation record so the "Not validated" gap there is closed. Handoff item 9 (whole-session gap). **Principle IX evidence — gates any claim the feature is production-done.**

> **T058 partial**: the "run CI live" half is **done** — workflow run 32537061874
> on PR #3 was the first live execution of `.github/workflows/ci.yml`, and all
> four jobs (backend, web, idle-cost guard, infra drift) ran and passed. Job logs
> were read to confirm the jobs do real work rather than passing vacuously. Still
> outstanding: the real `azd up`, the attack suite against a deployed API, and an
> interactive Entra External ID sign-in.


**Checkpoint**: Phase 9A can complete and merge without Azure. Phase 9B closes out
at the first real deploy; until then the feature is "verified in the build
environment", not "verified in production".

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **blocks all user stories**.
- **User Stories (Phases 3–6)**: all depend on Foundational. By priority: US1 → US2 → US4 → US3. US2/US4/US3 are independently testable and could be parallelized across developers once Foundational is done.
- **Web UI (Phase 7)**: depends on the corresponding API endpoints existing (T040←US1, T041←US2, T042←US3).
- **Polish (Phase 8)**: after the desired stories are complete.
- **Handoff Follow-Ups (Phase 9)**: after Phase 8. **9A** (T050–T054) is unblocked and can be worked now; **9B** (T055–T058) is gated on a real Azure deploy / live Entra tenant / CI runner. T056 depends on T055.

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
- Phase 9A: T050, T051, T052, T053, T054 in parallel (Program.cs, middleware/endpoints, contract test, AppDbContext.cs, provisioning middleware — T051 and T054 both touch `UserProvisioningMiddleware.cs`, so sequence those two if T054 is taken up).

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

### Handoff-driven closeout

Phase 9 exists because a session boundary, not a scope change, left this work
open. Work 9A first — it needs nothing this repo does not already have. Treat
9B as the first-real-deploy checklist: T055 (does the API container app
actually deploy, with AzureAd config?) is the P0 in the set, and T058 is the
Principle IX evidence that closes the "Not validated" gap in T048's record.

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
