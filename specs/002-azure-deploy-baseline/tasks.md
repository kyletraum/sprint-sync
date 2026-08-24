---

description: "Task list for Azure Deployment & Operability Baseline"
---

# Tasks: Azure Deployment & Operability Baseline

**Input**: Design documents from `/specs/002-azure-deploy-baseline/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: The spec does not request TDD. Test tasks appear only where the deliverable *is* a verification (US2, US4 fixtures, US5) — these are the feature's product, not optional extras.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US5, mapping to spec.md user stories
- **💸**: Requires a live deployment — **incurs or depends on real spend**
- **🔒**: Blocked on the External ID tenant (Phase 2)

---

## ⚠️ Read before starting

### Spend boundary

**Tasks T001–T009, all of Phase 6 (US4), T031–T040 (US3 code) and T016–T018 (web
hosting authoring) cost nothing.**
Spend begins at **T010 (`azd up`)** — the ~$5-8/month idle floor — and continues until
teardown. Everything marked 💸 sits on the far side of that line.

### Do not re-litigate

Per spec.md *Settled Decisions*, verified against azd 1.31.2:
`infra/api/api-containerapp.module.bicep` is azd's per-service deploy-time module.
**Do not wire it into `main.bicep`. Do not delete it.** `infra/` is generated —
never hand-edit it; declare in the AppHost and run `azd infra gen`.

### Two defects found during task generation

**1. Feature 001's T044 is falsely marked complete.** It reads *"Configure React
deployment to Azure Static Web Apps Free + `azd` wiring"* and is checked `[x]`, but
**no Static Web Apps configuration exists anywhere in the repository** — no workflow,
no Bicep, no azd service, no `swa-cli` config. `ci.yml`'s `web` job lints, tests and
builds; it does not deploy.

**Resolved by widening scope (2026-08-22).** Web hosting is now **in** scope so
SC-001 is met literally — a real person opens a hosted URL and signs in, with no
local tooling. This is new work, not re-validation of existing work. T059 corrects
001's record so the false completion does not propagate further.

Hosting is not just a deploy step. It forces two changes nothing else needed:

- **The API has no CORS configuration at all** (research R11). The SPA defaults to
  a *relative* `/api/v1`, which holds under the local dev proxy and breaks the moment
  the SPA is served from a different origin. Every browser call would be blocked at
  preflight — sign-in would appear to succeed and then fail on the first API call.
  A same-origin SWA proxy would avoid this but needs a linked backend, which needs
  **Standard** tier, which the constitution forbids. So: a named-origin CORS policy.
- **A three-step ordering** (research R11). The hosted origin does not exist until
  the Static Web App is provisioned, but it is needed for both the CORS policy and
  the identity redirect URI; and the API's URL is needed to build the SPA. Provision
  → configure → build and deploy. A single pass cannot satisfy this.

**2. `/health` is Development-only** and 404s in Production (research R2). The probe
tasks below target `/alive` and the **new** `/ready`. Pointing a readiness probe at
`/health` fails every check and hangs the deployment.

---

## Phase 1: Setup

**Purpose**: Local and subscription prerequisites. No spend.

- [ ] T001 Register the `Microsoft.AzureActiveDirectory` resource provider on subscription `0970d4fa-d0f5-4d4d-9cd0-5e8ac9ae976b` (`az provider register -n Microsoft.AzureActiveDirectory`), then poll `az provider show -n Microsoft.AzureActiveDirectory --query registrationState` until it reports `Registered`. Research R1 confirmed `ciamDirectories` is offered here; this converts that to fact.
- [ ] T002 Confirm the azd environment exists and targets the right subscription and region (`azd env list`, `azd env get-values`); create it with `azd env new <name> --subscription 0970d4fa-... --location <region>` if absent. Confirm `.azure/` stays untracked (azd writes its own `.gitignore`).

---

## Phase 2: Foundational — Entra External ID tenant

**Purpose**: The identity tenant that US1, US2 and US5 all block behind. No spend (External ID free tier).

**⚠️ Blocks US1, US2, US5. Does NOT block US3 or US4** — start Phase 6 (US4) and T031–T040 (US3 code) in parallel with this phase rather than waiting.

- [ ] T003 Create the Entra External ID (CIAM) tenant via the Azure portal (*Microsoft Entra External ID* → create external tenant). **Do not reuse "Default Directory"** — it is the MSA-backed directory owning the subscription; customers must not sign in to the directory that owns the billing account. Record the new tenant ID, which must differ from `az account show --query homeTenantId`.
- [ ] T004 Register the **API** application in the External ID tenant. Record the Application (client) ID and expose an Application ID URI / audience matching what `src/SprintSync.Api/` expects for `AzureAd__Audience`.
- [ ] T005 [P] Register the **web** application (SPA) in the External ID tenant. Configure the local Vite redirect URI for development now; the **hosted** redirect URI cannot be added until the Static Web App hostname exists (T019) and is added in T020.
- [ ] T006 Set the four identity values in the azd environment: `azd env set AZURE_AZURE_AD_INSTANCE / AZURE_AZURE_AD_TENANT_ID / AZURE_AZURE_AD_CLIENT_ID / AZURE_AZURE_AD_AUDIENCE`. Never commit these — they live in gitignored `.azure/<env>/.env`.
- [ ] T007 [P] Set `BUDGET_ALERT_EMAILS` (`azd env set BUDGET_ALERT_EMAILS '["<email>"]'`) **before** deploying. The `azure.yaml` postprovision hook skips the $20 backstop silently when unset (FR-015), and a skipped cost backstop is discovered on a bill.
- [ ] T008 Verify `scripts/check-azuread-config.sh` and `scripts/check-azuread-config.ps1` both pass. Note the known limitation: they check presence and placeholder-freedom, **not authenticity** — well-formed but wrong values pass here and fail later at sign-in.
- [ ] T009 Create two test users in two distinct organizations in the External ID tenant, for the US2 isolation proof. Record credentials securely; they are needed for T024–T028.

**Checkpoint**: Four values set, both guards green, two test identities exist.

---

## Phase 3: User Story 1 — An operator can sign in to a deployed Sprint Sync (P1) 💸

**Goal**: A running Sprint Sync in Azure that a real person can authenticate against.

**Independent test**: From a clean environment, follow [quickstart.md](./quickstart.md) start to finish and reach a working, signed-in deployment without consulting the implementers.

**⚠️ T010 is where spend begins.** Complete US4 and T031–T040 first so the first deploy already carries probes and telemetry — otherwise you deploy twice.

- [ ] T010 [US1] 🔒💸 Run `azd up` from the repository root. The preprovision hooks run the config guard then the cost guard, both fail-closed; provisioning must not begin until both pass. Record the deployed API URL and resource group.
- [ ] T011 [US1] 💸 Confirm the API container app provisioned and is running: `az containerapp show -n api -g rg-<env> --query "properties.runningStatus"`. A revision stuck not-Ready points at probe configuration (see US3) — not at identity.
- [ ] T012 [US1] 💸 Confirm all four `AzureAd__*` values are present and **non-empty** in the deployed container app's environment (`az containerapp show ... --query "properties.template.containers[0].env"`). Empty values mean the deployment **failed** even if `azd up` reported success (FR-006). This is the original T055 question from the 001 handoff.
- [ ] T013 [P] [US1] 💸 Confirm the $20 budget backstop provisioned with a 50% alert, and that the postprovision hook did not silently skip (FR-015).
- [ ] T014 [P] [US1] 💸 Confirm the deployed API rejects unauthenticated requests to the contracted endpoints with 401, proving the Entra configuration is active rather than merely present.
- [ ] T015 [US1] 💸 Confirm the database reaches its auto-paused state after idle, and that the container app scales to zero — the cost posture holding in reality, not just in Bicep (FR-014).
### Web hosting (research R10, R11, R12)

- [ ] T016 [US1] Author the Static Web App (Free tier) Bicep template in a **new `infra-web/` directory**, and invoke it from an `azure.yaml` postprovision hook the way `infra/budget.bicep` already is. **Do not put it in `infra/`** — that directory is generated by `azd infra gen` and the template would be erased, failing the CI infra-drift job. **Do not make it an azd service** — azd forbids pairing an Aspire service with a sibling, which is the documented reason the SPA was excluded originally (research R10). The declared-hook path is a first-class deploy path under constitution v1.1.0. The template MUST declare the **Free** SKU (FR-028). Decide and record whether `scripts/check-idle-cost.*` should extend its scan to `infra-web/` so that Free tier is machine-enforced rather than trusted — the guard currently scans `infra/` only, so this template would otherwise go unchecked.
- [ ] T017 [P] [US1] Add a named-origin CORS policy to `src/SprintSync.Api/`, with the permitted origin read from configuration rather than compiled in. **The API has no CORS configuration today** (research R11). **No wildcard** — this is a credentialed, tenant-scoped API, and `AllowAnyOrigin` with credentials is both insecure and rejected by browsers. This is the one carve-out from "no changes to 001's behaviour": it is deployment-enabling configuration, and no endpoint, DTO or isolation behaviour changes.
- [ ] T018 [P] [US1] Author `.github/workflows/deploy-web.yml` to build `web/sprint-sync-web` with `VITE_ENTRA_AUTHORITY`, `VITE_ENTRA_CLIENT_ID`, `VITE_API_SCOPE` and `VITE_API_BASE_URL` injected from repository secrets/variables, then deploy the built output to the Static Web App using its deployment token. Vite inlines these **at build time** (research R12) — they are not runtime configuration, so the SPA must be rebuilt whenever the tenant or API URL changes. Never commit the values (FR-029).
- [ ] T019 [US1] 💸 Provision the Static Web App and **record its hostname**. Nothing downstream can be configured until this exists — this is the head of the ordering chain in research R11 (FR-027).
- [ ] T020 [US1] 💸 Using the T019 hostname, set the API's permitted origin (redeploying the API so it takes effect) and add the hosted redirect URI to the web app registration from T005 (FR-030, FR-031). Then run the T018 workflow to build and deploy the SPA with the deployed API URL from T010.

- [ ] T021 [US1] 💸 Validate interactive sign-in against the **hosted** web app: open the Static Web App URL in a browser, sign in with a test identity from T009, and confirm JIT provisioning creates the user and shows their own organization (FR-027, SC-001, US1 scenario 6). **Verify in a browser, not with curl** — curl does not enforce CORS, so it will pass while the SPA fails.
- [ ] T022 [US1] Review [quickstart.md](./quickstart.md) against what actually happened and correct every step that misled, including real resource names and real error text. SC-002 requires an operator to succeed **without** implementer help; the only evidence is a walkthrough that matches reality.

**Checkpoint**: Deployed, configured, reachable, and a real person has signed in.

---

## Phase 4: User Story 2 — Isolation is proven against the deployed system (P1) 💸

**Goal**: The Principle IX evidence feature 001 has never had. **This gates any claim either feature is production-done.**

**Independent test**: Cross-tenant access is refused on the deployed API, with non-existence indistinguishable from non-membership.

- [ ] T023 [US2] 💸 **Settle research R7** — the deliberately deferred decision — now that a deployment and real tokens exist. Determine how the public-contract cross-tenant cases in `tests/SprintSync.Api.Tests/Isolation/CrossTenantAccessTests.cs` are retargeted at the deployed URL with real bearer tokens. Note that `QueryFilterTests.cs` and `TenantWriteGuardTests.cs` assert **persistence-layer internals** through a throwaway entity and are white-box by design — they are **not** meaningfully re-runnable black-box and stay where they are. Record the decision in this file before writing code against it.
- [ ] T024 [US2] 💸 Acquire real bearer tokens for both T009 test users from the External ID tenant, by a repeatable method (documented, not hand-copied from a browser session).
- [ ] T025 [US2] 💸 Seed or confirm two organizations exist in the deployed system, one per test user, with at least one resource each.
- [ ] T026 [US2] 💸 Prove a member of org A cannot read org B's resources through the deployed public API (spec US2 scenario 1).
- [ ] T027 [US2] 💸 Prove **indistinguishability**: a request for a genuinely non-existent resource and a request for another org's resource return responses a caller cannot tell apart — status, body, and headers (spec US2 scenario 2, FR-023). This is the hide-existence guarantee; a difference here is a disclosure defect.
- [ ] T028 [US2] 💸 Prove the `X-Organization-Id` act-as hint is ignored when the caller is not a verified member, falling through to their genuine active organization (spec US2 scenario 3).
- [ ] T029 [US2] 💸 Run the full retargeted suite and confirm **every** case passes (FR-022). A partial pass is a failure.
- [ ] T030 [US2] Record the validation evidence per [data-model.md](./data-model.md) §4 — what ran, against which deployment URL, tenant, revision and commit, when, and the result.

**Checkpoint**: Isolation is proven, not asserted. 001's Principle IX gate is dischargeable.

---

## Phase 5: User Story 3 — Operators can tell whether the system is healthy (P2)

**Goal**: Principle X satisfied — the platform calls the app's own health endpoints, and traces are queryable.

**Independent test**: The platform reports health from the application's endpoints; exercising the API produces queryable traces.

**T031–T040 are offline-buildable — do them before T010 so the first deploy carries them.**

### Readiness endpoint and gate (research R2, R3)

- [x] T031 [P] [US3] Add a `ready`-tagged, **database-free** startup-gate health check in `src/SprintSync.ServiceDefaults/Extensions.cs` (`AddDefaultHealthChecks`), reporting Unhealthy until startup work completes and Healthy thereafter, backed by an in-memory flag. **It must not open a database connection** — the database is free-serverless with `AutoPauseDelay = 60`, and a per-probe connection would keep it awake permanently, breaching the Deployment & Cost Constraints silently (research R3). Only a `live`-tagged `"self"` check exists today; there is no `ready` tag to reuse.
- [x] T032 [US3] Map `GET /ready` in `MapDefaultEndpoints` in `src/SprintSync.ServiceDefaults/Extensions.cs`, in **all** environments, filtered to the `ready` tag, returning **status only** with no per-check detail. Leave `/health` Development-only — its restriction is a deliberate security decision (https://aka.ms/aspire/healthchecks) and `/ready`'s no-detail response is what makes it safe in Production. Contract: [contracts/health-endpoints.md](./contracts/health-endpoints.md).
- [x] T033 [US3] Signal startup-complete to the readiness gate from `src/SprintSync.Api/Program.cs`, after migration and seeding finish, so readiness correctly withholds traffic during the migration window (FR-017).

### Telemetry export (research R5)

- [x] T034 [P] [US3] Add the `Azure.Monitor.OpenTelemetry.AspNetCore` package to `src/SprintSync.ServiceDefaults/SprintSync.ServiceDefaults.csproj`. The exporter stub at `Extensions.cs:91-94` is commented out precisely because this package is absent, so this is a package addition, not just uncommenting.
- [x] T035 [US3] Enable the Azure Monitor exporter in `AddOpenTelemetryExporters` in `src/SprintSync.ServiceDefaults/Extensions.cs`, gated on non-empty `APPLICATIONINSIGHTS_CONNECTION_STRING`. Absence must leave startup unaffected (FR-019), and export failure must never become a request or health failure (FR-020).
- [x] T036 [US3] Declare an Application Insights resource in `src/SprintSync.AppHost/AppHost.cs` in publish mode and reference it from the `api` resource so `APPLICATIONINSIGHTS_CONNECTION_STRING` reaches the container app. Declare it in the AppHost, never by hand-editing `infra/`. Cost: workspace-based App Insights has no standing resource fee and bills on ingestion into the Log Analytics workspace `cae.module.bicep:30` already creates (research R5).

### Probe wiring (research R4)

- [x] T037 [US3] Add liveness and readiness probes to the container app template inside the **existing `api.PublishAsAzureContainerApp(...)` callback** in `src/SprintSync.AppHost/AppHost.cs:83-89`, alongside the `MinReplicas`/`MaxReplicas` settings already there. **Not `ConfigureInfrastructure`** — that is used for the SQL resource; the spec and handoff both name it wrongly (research R4). Liveness → `/alive`, readiness → `/ready`, both on the container's HTTP port; do not hardcode a port that could diverge from `api_containerport`.
- [x] T038 [P] [US3] Correct the stale constitution reference at `src/SprintSync.AppHost/AppHost.cs:3`, which cites "Principle X" for topology. Since 2026-08-22, Principle X is *Operable by Default*; topology is a Technology & Platform Constraint. The comment now cites Principle X to mean something it does not say, in the very file implementing Principle X (research R9).
- [x] T039 [US3] Run `azd infra gen --force` and confirm the regenerated `infra/` contains the probes and the Application Insights resource, and that `git diff` shows only intended changes. `infra/` is generated: if probes do not appear here, they were declared in the wrong place.
- [ ] T040 [US3] **(PARTIAL — cost-guard half done, `dotnet test` BLOCKED locally: Docker not running, so Testcontainers cannot start SQL. CI's backend job will run it.)** Confirm `dotnet test` stays green and `scripts/check-idle-cost.*` still passes after the App Insights addition — no idle-billable resource may have crept in (FR-014).

### Deploy-gated verification

- [ ] T041 [US3] 💸 Confirm the ACA revision reaches Ready and the platform is calling both probes — not assuming a started process is healthy (FR-016, US3 scenario 1).
- [ ] T042 [P] [US3] 💸 Confirm against the deployed URL that `/alive` returns 200, `/ready` returns 200, and `/health` returns **404**. The 404 is correct behaviour, not a defect.
- [ ] T043 [US3] 💸 Confirm readiness gates traffic: during startup/migration the instance reports not-ready and receives no traffic (FR-017, US3 scenario 2).
- [ ] T044 [P] [US3] 💸 Exercise the deployed API, then confirm a trace for a specific request is queryable in Application Insights within 5 minutes (FR-018, SC-006).
- [ ] T045 [US3] 💸 Confirm the readiness probe has **not** defeated database auto-pause — the database must still reach its paused state while a replica is alive. Failure here is a live cost breach, not a test failure (research R3).

**Checkpoint**: The platform can see the service, and the cost posture survived making it visible.

> **US3 code-half record (2026-08-22)** — T031–T039 complete; T040 partial.
>
> **Generated Bicep confirms the wiring reached the deploy** (T039). Both probes
> emit with `port: int(api_containerport)` — the *same expression* the ingress
> `targetPort` uses, because the AppHost binds `app.Configuration.Ingress.TargetPort`
> rather than a literal, so the two cannot drift apart. `APPLICATIONINSIGHTS_CONNECTION_STRING`
> flows from `insights_outputs_appinsightsconnectionstring`, and `infra/insights/`
> is wired into `main.bicep` — so the cost guard classifies it `provision`, not
> `none`.
>
> **Cost guard still green on the regenerated infra**, both twins, plus 15/15 on
> the fixture suite. Adding Application Insights introduced no idle-billable
> resource (FR-014).
>
> **Verified locally**: `StartupGateTests` 4/4 pass — the gate reports not-ready
> before `MarkReady()`, ready after, and `MarkReady()` is idempotent.
>
> **NOT verified locally**: `HealthEndpointContractTests` (5 tests, incl. the
> `/health`-404-in-Production tripwire) need Testcontainers, and **Docker is not
> running on this machine**. They compile and are wired into the suite; CI's
> backend job will execute them. Claiming them green here would be asserting
> something unobserved.
>
> Package additions: `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0
> (ServiceDefaults — the exporter stub genuinely could not compile without it)
> and `Aspire.Hosting.Azure.ApplicationInsights` 13.4.6 (AppHost).

---

## Phase 6: User Story 4 — The cost guard cannot be satisfied by undeployed infrastructure (P2)

**Goal**: The guard reports on what is actually deployed. **Fully offline — start immediately, in parallel with Phase 2.**

**Independent test**: A template on no deploy path fails the guard; the real `infra/` still passes.

Contract: [contracts/cost-guard-cli.md](./contracts/cost-guard-cli.md). Classification model: [data-model.md](./data-model.md) §2.

- [x] T046 [US4] Implement three-path deploy classification in `scripts/check-idle-cost.sh`: **provision** (transitively reachable from `infra/main.bicep` via `module <name> '<relative-path>'`), **service-deploy** (declares `Microsoft.App/containerApps` *and* takes a container-image parameter), **hook** (path appears in an `azure.yaml` hook command). Classify every `.bicep` under the infra path before any posture check runs.
- [x] T047 [US4] Scope the posture checks to classified templates in `scripts/check-idle-cost.sh`: `saw_containerapp` and `saw_zero_floor` may be set **only** from templates on a deploy path, so presence under `infra/` can no longer substitute for deployment (FR-009). Preserve every existing fail-closed guarantee.
- [x] T048 [US4] Fail the guard on any template classified `none`, naming the specific file (FR-010, FR-013).
- [x] T049 [US4] Port T046–T048 to `scripts/check-idle-cost.ps1`, behaviourally identical (FR-012). CI runs the POSIX twin and developers run the Windows twin — divergence means a developer's green is not CI's green.
- [x] T050 [P] [US4] Verify **no false positives on the real repository**: the guard exits 0 with all eight templates classified — six provision, `api/api-containerapp.module.bicep` as service-deploy, `budget.bicep` as hook. These two are exactly what a naive reachability test would wrongly condemn (FR-011, research R6). A guard that fails every legitimate run gets disabled, which is worse than the bug it fixes.
- [x] T051 [P] [US4] Verify with a planted fixture that a `.bicep` on no deploy path, declaring a container app, **fails** the guard and is named in the output.
- [x] T052 [P] [US4] Verify that a no-path template cannot satisfy `saw_zero_floor` — if the only `minReplicas: 0` declaration lives in an unclassified template, the guard must fail rather than pass.
- [x] T053 [US4] Verify twin parity: both implementations produce identical verdicts and identical failure sets across every fixture in T050–T052.

**Checkpoint**: The guard can no longer be satisfied by files that contribute nothing to the running system.

> **US4 validation record (2026-08-22)** — complete and verified.
>
> Suite: `tests/cost-guard/run-tests.sh`, five fixtures, **15/15 assertions pass**,
> both twins agreeing on every case. Run it with `sh tests/cost-guard/run-tests.sh`.
>
> | Fixture | Asserts | Verdict |
> |---|---|---|
> | `all-paths-ok` | no false positive on a service-deploy module or a hook-deployed template | exit 0 |
> | `orphan-containerapp` | a container app on no deploy path fails, **named** | exit 1 |
> | `orphan-only-zero-floor` | an unclassified template cannot satisfy `saw_zero_floor` | exit 1 |
> | `min-replicas-one` | the original posture check still fires on deployed templates | exit 1 |
> | `empty-infra` | fail-closed preserved | exit 1 |
>
> Real repository: both twins exit 0, classifying all eight templates —
> six `provision`, `api/api-containerapp.module.bicep` as `service-deploy`,
> `budget.bicep` as `hook`. Zero classify as `none`.
>
> **A real bug was found and fixed while building this.** The first
> implementation of `norm_path` piped `printf '%s'` (no trailing newline) into
> `while read`, so `read` hit EOF, the loop body never ran, and the function
> returned **empty**. `in_set ""` then matched the empty line `printf` emits for
> an empty set, so **every template classified as `provision`** and the guard
> would have passed anything at all — the exact vacuous-pass failure mode US4
> exists to eliminate. Caught by testing that hook detection was load-bearing
> (removing `azure.yaml` must make `budget.bicep` fail) rather than by trusting
> a green run. `in_set` now rejects empty needles explicitly, with a comment
> saying why.
>
> **Deferred, recorded** (T016): whether the guard should extend its scan to
> `infra-web/`. It currently scans `infra/` only, so the Static Web App's Free
> tier would be trusted rather than machine-enforced.

---

## Phase 7: User Story 5 — Continuous integration runs for real (P3)

**Goal**: CI is trustworthy evidence. **Mostly already satisfied** — run 32537061874 on PR #3 ran all four jobs green with logs read to confirm non-vacuous work. Only the below remain.

- [ ] T054 [US5] Prove CI can **fail**: open a throwaway pull request containing one deliberately broken test, confirm the pipeline goes red, then close it without merging (research R8). A green pipeline nobody has seen go red is indistinguishable from one that always passes — this is the assumption under test, so it cannot be assumed.
- [ ] T055 [US5] Re-confirm CI passes with this feature's changes applied, paying particular attention to the `cost-guard` job (whose behaviour Phase 6 changes) and the `infra-drift` job (which must accept the regenerated `infra/` from T039).

**Checkpoint**: CI protects what the other stories established.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T056 [P] Update [quickstart.md](./quickstart.md) with real resource names, real timings and the actual error text encountered, so SC-002's "without consulting the implementers" claim is backed by a document that matches reality.
- [ ] T057 Close feature 001's Principle IX gate: record the T030 validation evidence against the **T048 validation record** in `specs/001-org-tenant-context/tasks.md`, replacing its "**Not validated**: the signed-in browser walkthrough" note with what was actually proven — and with what was not (see defect 1). FR-026, SC-009.
- [ ] T058 [P] Confirm observed spend over at least one full idle day sits within the constitution's ~$5-8/month floor (SC-004). Assertion is not evidence; the budget alert is a backstop, not a measurement.
- [ ] T059 Correct feature 001's **T044**, currently marked `[x]` complete for "Configure React deployment to Azure Static Web Apps Free + `azd` wiring" though no SWA configuration exists in the repository. Either unmark it with a note explaining what remains, or record explicitly that SWA hosting was descoped and where it now lives. Leaving a false completion in the record is how this defect reached 002's Out of Scope section unchallenged.
- [ ] T060 If the session ends with open items, create `.specify/handoffs/<yyyy-mm-dd>-<slug>.md` per `.specify/handoffs/README.md`, listing what remains and why. If nothing is open, note that no handoff was needed.

---

## Dependencies & Execution Order

### Story dependencies

```
Phase 1 (Setup) ──► Phase 2 (Tenant) ──┬──► US1 (deploy) ──┬──► US2 (isolation)
                                        │                   └──► US3 verify (T041-T045)
                                        └──► US5 (T054 needs no tenant)

US4 (cost guard) ─── fully independent, no tenant, no deploy
US3 code (T031-T040) ─── independent of the tenant; needed BEFORE the first deploy

Web hosting ordering chain (research R11) — strictly sequential, cannot be collapsed:

  T016/T017/T018 (author, offline)
        └──► T019 provision SWA ──► hostname exists
                 └──► T020 CORS origin + redirect URI + build & deploy SPA
                          └──► T021 hosted sign-in
```

### Critical path

`T001 → T003 → T006 → T008 → T010 → T012 → T021 → T023 → T029 → T030 → T057`

Everything else is parallelisable around it.

### Recommended execution order

This differs from phase order, deliberately — it minimises spend and avoids a
second deploy cycle.

| Order | Work | Spend |
|---|---|---|
| 1 | T001–T002 (setup) | $0 |
| 2 | **In parallel**: T003–T009 (tenant) ‖ Phase 6 US4 ‖ T031–T040 (US3 code) ‖ **T016–T018 (hosting authoring)** ‖ T054 (CI can fail) | $0 |
| 3 | T010 — **first deploy, already carrying probes and telemetry** | 💸 begins |
| 4 | T011–T015 (API verify), **T019→T020→T021 (hosting chain, sequential)**, T022, T041–T045 (US3 verify) | 💸 |
| 5 | T023–T030 (US2 isolation proof) | 💸 |
| 6 | T055, Phase 8 polish | 💸 |

Doing US4 and US3's code **before** T010 means the first deploy is the only deploy.
Deploying first and adding probes after costs an extra full cycle for nothing.

### Parallel opportunities

- **Phase 2 ‖ Phase 6 ‖ T031–T040**: the largest win. US4 is entirely tenant-free and deploy-free; US3's code half only needs a compiler. Neither should idle behind tenant creation.
- Within Phase 6: T050, T051, T052 in parallel once T046–T049 land.
- Within US3 code: T031 ‖ T034 ‖ T038 (different files); T032/T033 follow T031; T035 follows T034.
- Within US1: T013, T014 in parallel after T011. T016 ‖ T017 ‖ T018 in parallel (different files, all offline).
- **Not parallelisable**: T019 → T020 → T021. Each needs the previous one's output — the hostname, then the configured origin and deployed content.
- Within US3 verification: T042, T044 in parallel after T041.

### Blocking notes

- **T023 blocks T024–T029.** Research R7 is deliberately unresolved; settle it with a deployment and real tokens in hand rather than guessing a harness that cannot be executed.
- **T009 blocks T024.** Two real identities in two organizations are needed before any isolation attempt.
- **T039 blocks T055.** The `infra-drift` CI job diffs regenerated infra against what is committed.
- **T019 blocks T020 blocks T021.** The hosted origin does not exist until the Static Web App is provisioned, and it is required by both the API's CORS policy and the identity redirect URI. Attempting these in one pass fails (research R11).
- **T010 blocks T020.** The SPA build needs the deployed API URL injected at build time; Vite inlines it, so it cannot be supplied afterwards (research R12).
- **T017 must reach the deployed API before T021.** A CORS policy sitting in source does nothing — the API has to be redeployed with the real origin configured, or the browser blocks every call while curl still passes.

---

## Implementation Strategy

### MVP scope

**US1 alone is the MVP** — a deployed, reachable Sprint Sync someone can sign in to.
It requires Phases 1–3, and in practice US4 plus T031–T040 first so the deploy is
done once.

But **US1 alone should not be called production-done.** US2 is equal-priority P1
precisely because a deployed multi-tenant system whose isolation has never been
proven outside a test harness is one nobody should trust. Ship US1 to see it work;
ship US2 before believing it.

### Incremental delivery

1. **Free increment** — US4 complete, US3 code written, tenant standing. Real value:
   the cost guard stops lying, and the identity tenant exists. Zero spend.
2. **First deploy** — US1 plus US3 verification. The system is real and observable.
3. **Evidence increment** — US2. 001's Principle IX gate closes; both features become
   claimable.
4. **Durability** — US5 and polish.

### On the two defects

Neither was invented here; both were found by reading the code the tasks touch.

**Defect 1** (001's T044 falsely complete) was resolved by widening scope rather
than by working around it — SC-001 is now met literally. That widening was not
free: it surfaced that **the API has no CORS configuration at all**, which no
other task would have needed, and it introduced the only strictly-sequential
chain in the feature (T019 → T020 → T021). Both are consequences of hosting being
real work that 001's record claimed was already done.

**Defect 2** (`/health` 404s in Production) would have been a crash-looping
deployment had the probes been wired as the handoff described.

The common thread is worth noting for the next feature: both defects were
**claims in a document contradicted by the code**. The handoff asserted a probe
target that does not exist in Production; 001's task list asserted a deployment
that does not exist at all. Task generation caught them only because it read the
files rather than trusting the descriptions of them.
