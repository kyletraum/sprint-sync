# Implementation Plan: Azure Deployment & Operability Baseline

**Branch**: `002-azure-deploy-baseline` | **Date**: 2026-08-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-azure-deploy-baseline/spec.md`

## Summary

Take Sprint Sync from "green locally" to "provably running in Azure". Stand up an
Entra External ID tenant, deploy with `azd up`, wire health probes and telemetry
so the platform can see the service, fix the cost guard so it reports on what is
actually deployed, and prove tenant isolation against the deployed API — which is
feature 001's outstanding Principle IX evidence.

The plan's central sequencing problem: three of five stories block behind an
identity tenant that does not exist, while two are buildable today. Phase 1 below
resolves the tenant question **first and cheaply** (a provider registration, no
spend), so the offline work is never idle-blocked behind it and the riskiest
assumption fails early if it is going to fail.

## Technical Context

**Language/Version**: C# / .NET 10, TypeScript (React 19 + Vite) — unchanged by this feature

**Primary Dependencies**: .NET Aspire (AppHost as topology source of truth), Azure Developer CLI (azd) 1.31.2, Azure CLI, EF Core, Bicep (generated, not hand-authored)

**New dependency**: `Azure.Monitor.OpenTelemetry.AspNetCore` in `SprintSync.ServiceDefaults` — required by the exporter stub at `Extensions.cs:91-94`, currently absent from the `.csproj`

**Storage**: Azure SQL, free serverless with auto-pause (`GP_S_Gen5_1`, `AutoPauseDelay = 60`) — unchanged, but see the Principle X conflict below

**Testing**: xUnit + Testcontainers (backend, 56 tests), vitest (web, 5 tests), `check-idle-cost.{sh,ps1}` guards, GitHub Actions CI (4 jobs, green on run 32537061874)

**Target Platform**: Azure Container Apps (Consumption, `minReplicas: 0`), Azure Static Web Apps Free (web, out of scope here), Entra External ID (to be created)

**Project Type**: Web service + SPA, deployed via azd from an Aspire AppHost

**Performance Goals**: None new. Cold-start and database-resume latency after idle are explicitly accepted by the constitution.

**Constraints**:
- Idle cost within the ~$5-8/month floor (ACR Basic + Log Analytics); budget backstop $20, alert at 50%
- No idle-billable dependency may be introduced
- **Readiness probes must not touch the database** — auto-pause is 60s, and a database-touching probe would keep it awake permanently (see R3)
- Probe misconfiguration crash-loops the container; `/health` is Development-only and 404s in Azure (see R2)

**Scale/Scope**: One environment, one region, single replica ceiling. Demo scale.

**Unknown carried forward**: how the cross-tenant attack suite is retargeted at a deployed API (R7). Deliberately deferred — see Complexity Tracking.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against **constitution v1.1.0** (merged 2026-08-22, PR #4), which added
Principle X and the three-deploy-path verification rule this feature implements.

| Principle | Applies | Assessment |
|---|---|---|
| I. API as the Sole Data Path | Indirect | No new data path. Deployment only. **PASS** |
| II. Stable, Versioned Contract | No | No contract change. **PASS** |
| III. Documented Contract Conventions | Indirect | `/ready` is an operational endpoint, not a data endpoint — outside the OpenAPI contract, consistent with existing `/alive`. **PASS** |
| IV. Multi-Tenancy by Design | No | No entity changes. **PASS** |
| V. Isolation at the Persistence Layer | **Yes — validates** | This feature does not change isolation; it produces the first evidence isolation holds on a deployed system. **PASS** |
| VI. Server-Resolved, Verified Tenant Context | **Yes — validates** | Real tokens from a real tenant exercise resolution for the first time. **PASS** |
| VII. Identity and Authorization Separate | Indirect | External ID authenticates; membership still authorizes. Unchanged. **PASS** |
| VIII. Unified, Policy-Based Authorization | No | No policy changes. **PASS** |
| IX. Contract-First, Isolation-Proven Testing | **Yes — discharges 001's gate** | US2 runs the cross-tenant attack suite against the deployed API. **PASS, conditional** — see gate note below |
| X. Operable by Default | **Yes — primary** | US3 delivers probes actually called by the platform, plus telemetry export when a destination is supplied. **PASS with a resolved conflict** — see below |
| Technology & Platform Constraints | Yes | Probes and telemetry declared in the AppHost, never hand-written into generated Bicep. **PASS** |
| Deployment & Cost Constraints | **Yes — primary** | US4 makes the guard enforce the revised three-path rule. New App Insights adds no standing cost (R5). **PASS** |

### Resolved conflict: Principle X vs Deployment & Cost Constraints

Principle X requires a readiness probe. The obvious implementation — a database
connectivity check — **would violate the Deployment & Cost Constraints** by
keeping the free-serverless database awake against its 60-second auto-pause, and
would do so silently, surfacing as a bill rather than a failure.

Resolved in R3 with a database-free startup gate: readiness reports unready until
startup work (including migration) completes, then healthy, reading an in-memory
flag. No per-probe database traffic, and it describes exactly the state readiness
exists to describe. Principle X is satisfied in substance, not merely in form.

This is recorded as a resolution, **not** an exception — no principle is waived.

### Gate note on Principle IX

Feature 001 currently satisfies Principle IX only against Testcontainers. This
feature carries that gate. Until US2 passes, **neither 001 nor 002 may be called
production-done**. Recorded in 001's `tasks.md` Phase 9B and in this spec's
FR-026 / SC-009.

**Result: PASS.** No unjustified violations. Nothing in Complexity Tracking is a
constitution violation.

## Project Structure

### Documentation (this feature)

```text
specs/002-azure-deploy-baseline/
├── plan.md              # This file
├── research.md          # Phase 0 output — R1-R9
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output — operator setup + validation guide
├── contracts/           # Phase 1 output
│   ├── health-endpoints.md
│   └── cost-guard-cli.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (16/16)
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

Files this feature touches. It adds no new project and no new source directory.

```text
src/
├── SprintSync.AppHost/
│   └── AppHost.cs                  # EDIT: probes in PublishAsAzureContainerApp (R4);
│                                   #       App Insights resource (R5);
│                                   #       fix stale "Principle X" comment (R9)
├── SprintSync.ServiceDefaults/
│   ├── Extensions.cs               # EDIT: /ready endpoint (R2); ready-tagged startup
│   │                               #       gate (R3); un-stub Azure Monitor (R5)
│   └── SprintSync.ServiceDefaults.csproj   # EDIT: add Azure.Monitor.OpenTelemetry.AspNetCore
└── SprintSync.Api/
    └── Program.cs                  # EDIT: signal startup complete to the readiness gate

scripts/
├── check-idle-cost.sh              # EDIT: three-path classification (R6)
└── check-idle-cost.ps1             # EDIT: identical twin

infra/                              # GENERATED — never hand-edited.
                                    # Regenerated by `azd infra gen`; the CI
                                    # infra-drift job diffs it.

tests/
└── SprintSync.Api.Tests/
    └── Isolation/                  # US2 target — retargeting approach deferred (R7)

.github/workflows/ci.yml            # Re-confirm after changes; prove it can fail (R8)
```

**Structure Decision**: No structural change. This feature edits configuration,
guards, and operability wiring in the existing Aspire solution. The one hard rule
is that `infra/` stays generated — probes and telemetry are declared in the
AppHost and reach Bicep through `azd infra gen`, or the CI infra-drift job will
correctly reject them.

## Implementation Phases

Ordered so the riskiest assumption is tested first and cheaply, and so
offline-buildable work is never blocked behind a live dependency.

### Phase 1 — De-risk the tenant (no spend, do first)

Register `Microsoft.AzureActiveDirectory`, create the External ID tenant, and
register the API and web applications. R1 establishes this is offered on the
subscription; this phase converts "should work" into "does work".

**Why first**: it is the only task that can invalidate the feature's central
assumption, it costs nothing, and Stories 1, 2, and 5 all block behind it. If it
fails, that must be known before anything is built on top of it.

**Deliverable**: the four `AZURE_AZURE_AD_*` values, set in the azd environment,
with `check-azuread-config` passing. → *US1 scenario 1*

### Phase 2 — Offline work (runs in parallel with Phase 1; still no spend)

Everything buildable without a deployment. Independent of Phase 1's outcome, so
it proceeds even if the tenant hits trouble.

- **Cost guard three-path classification** (R6) — both twins, plus fixture-based
  tests that a no-path template fails and the real `infra/` passes. → *US4, complete*
- **`/ready` endpoint + database-free startup gate** (R2, R3) → *US3, code half*
- **App Insights resource + un-stubbed exporter + package** (R5) → *US3, code half*
- **Probes in `PublishAsAzureContainerApp`** (R4) → *US3, code half*
- **Stale `Principle X` comment fix** (R9)

**Deliverable**: `azd infra gen` regenerates cleanly, the cost guard passes on
real infra and fails on a planted no-path template, all existing tests stay green.

### Phase 3 — First deploy (spend starts here)

**This is where real money begins** — the ~$5-8/month idle floor commences at
`azd up` and continues until the resources are deleted. Everything before this
point is free; nothing after it is.

Set `BUDGET_ALERT_EMAILS` **before** deploying, so the $20 backstop provisions
with the topology instead of being a forgotten manual step (the postprovision
hook skips silently without it — FR-015).

- `azd up`; confirm the container app provisions and does not crash-loop
- Confirm all four `AzureAd__*` values are non-empty at runtime → *US1 scenarios 2-3*
- Confirm probes work against the real deployment — the crash-loop risk in R2
  is only truly retired here → *US3 scenario 5*
- Confirm traces are queryable → *US3 scenario 3*
- Interactive sign-in through the web app → *US1 scenario 4*

### Phase 4 — Prove isolation (the Principle IX gate)

Retarget the public-contract cross-tenant cases at the deployed API with real
tokens for two users in two organizations. **R7 must be settled at the start of
this phase**, with a deployment and tokens in hand.

Record the result against 001's T048 validation record. → *US2 entire; closes
FR-025, SC-009*

### Phase 5 — Close CI

Prove CI can fail via a throwaway PR (R8), and re-confirm green with this
feature's changes applied — particularly the cost-guard job, whose behaviour
Phase 2 changes. → *US5 remaining scenarios*

### Cost checkpoint

| Phase | Spend |
|---|---|
| 1 — tenant | $0 (External ID free tier) |
| 2 — offline | $0 |
| **3 — deploy** | **~$5-8/mo floor begins** |
| 4-5 | No additional standing cost |

## Complexity Tracking

> No constitution violations. This section records one deliberate deferral.

| Item | Why deferred | Why not decide now |
|---|---|---|
| R7 — how the attack suite retargets at a deployed API | The three isolation test files are not equivalent: `CrossTenantAccessTests` asserts public-contract behaviour and can retarget; `QueryFilterTests` and `TenantWriteGuardTests` assert persistence-layer internals through a throwaway entity and are white-box **by design**. FR-022's "attack suite" realistically means the first. | Deciding the token-acquisition and harness approach without a deployed API or a real tenant would be guessing, and a wrong guess here writes tasks that cannot be executed. Settled at the start of Phase 4, when both exist. |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Probe misconfiguration crash-loops the container | Medium | High — deployment down | R2 identified that `/health` 404s in Production before any code was written. Deploy probes on a revision that can be rolled back; verify against the real deployment (US3 scenario 5). |
| Readiness check defeats SQL auto-pause | **Was near-certain** | High — silent cost breach | Resolved in design (R3): database-free startup gate. Confirm against observed spend (SC-004). |
| External ID tenant creation blocked by quota/region | Low | High — blocks US1, 2, 5 | Phase 1 is first and free. Phase 2 proceeds regardless. |
| App Insights ingestion exceeds the free grant | Low | Medium | Demo scale, scale-to-zero at idle. Confirm via SC-004; add sampling if non-trivial. |
| Hand-editing generated Bicep | Low | Medium | CI infra-drift job catches it. Declare in the AppHost only. |
| Re-litigating the "orphaned module" | Medium | Medium — wasted work, or a broken deploy if acted on | Recorded as Settled in spec.md and carried into R6. Do not wire it into `main.bicep`; do not delete it. |
