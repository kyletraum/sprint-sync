---
id: 2026-08-22-phase-9b-deploy-gated
created: 2026-08-22
feature: 001-org-tenant-context
branch: main
status: open
source: Phase 9A session (consumed 2026-08-21-org-tenant-committee-followups, shipped PR #3)
consume_in: [tasks, implement]
---

## Context

The previous handoff (`2026-08-21-org-tenant-committee-followups`) was consumed:
its 9 items became Phase 9 in `specs/001-org-tenant-context/tasks.md`, split into
9A (buildable without Azure) and 9B (deploy-gated). **Phase 9A is done, CI-verified
and merged** — PR #3, commit `2bc29dc` on `main`.

What remains is Phase 9B plus two items this session surfaced. Everything here is
already task-shaped in `tasks.md`; this handoff exists so the open items survive the
session boundary, per the protocol in `.specify/handoffs/README.md`.

## Verified baseline (known-good, do not re-verify from zero)

- **CI works.** Workflow run 32537061874 was the first live execution of
  `.github/workflows/ci.yml`. All four jobs ran and did real work (job logs read,
  not just conclusions trusted): backend build + tests, web, idle-cost guard,
  infra drift check.
- Backend: `Build succeeded. 15 Warning(s) 0 Error(s)` (Release, .NET 10) and
  `Passed! - Failed: 0, Passed: 57, Skipped: 0, Total: 57` against real SQL via
  Testcontainers. 57 = the 56 local baseline, less the excluded `Category=Timing`
  test, plus the two new T052 contract tests.
- Web: 5/5 vitest, Prettier + ESLint clean, `tsc -b && vite build` clean.
- The T050 `sp_getapplock` path executes on every `WebApplicationFactory` boot in
  the suite, so it has been exercised against real SQL Server many times.

## Decisions already made (do not re-litigate)

Everything in the prior handoff's decision list still stands (scoped non-pooled
DbContext, hide-existence 404, `X-Organization-Id` as a membership-verified soft
hint, create-only JIT provisioning, the ~$5-8/mo idle floor, write-guard covering
insert/update/delete). Added this session:

- **The tenant query filter is now an idiomatic context-instance-member lambda**
  (`AppDbContext.ApplyTenantFilter<TEntity>`), not a hand-built expression tree.
  `QueryFilterTests` — including `Filter_IsReEvaluatedPerContextInstance` — proves
  per-instance re-evaluation survived. Do not revert to the reflection form.
- **Migrations run under a session-scoped SQL application lock**
  (`Data/StartupMigrator.cs`). Failing to acquire fails startup deliberately, so the
  platform retries rather than two revisions applying DDL concurrently.
- **The served OpenAPI document now declares the 401/400 responses** the contract
  always promised. `OpenApiContractTests` holds file and served document equal on
  response status codes and request-body shape, so this cannot silently regress.
- **T054 (refresh `DisplayName` on repeat login) was deliberately not taken up.**
  Create-only JIT provisioning is settled and the reviewer marked it "no change
  required". It stays open only so the decision is visible, not because it is owed.

## Outstanding items

| # | Priority | Area | Item | Suggested phase | Notes |
|---|----------|------|------|-----------------|-------|
| 1 | **P0** | deploy | Real `azd up`: confirm the API container app provisions **and receives non-empty `AzureAd__*` env**. Determine whether it deploys from `infra/main.bicep` or from the azd-applied module at `azd deploy`, then wire in or delete `infra/api/api-containerapp.module.bicep`, which is unreachable from `main.bicep` (which wires only api-identity, api-roles-sql, cae, cae-acr, sql). | implement | tasks.md **T055**. Do NOT hand-wire `main.bicep` blind — it may conflict with azd's own deploy model. Blocks #2. |
| 2 | P1 | deploy/CI | After #1, extend `scripts/check-idle-cost.{sh,ps1}` to reject `Microsoft.App/containerApps` modules not reachable from `infra/main.bicep`. | tasks | tasks.md **T056**. PR #3's green cost-guard job is a live demonstration of the gap: the guard greps every `*.bicep` under `infra/` with no reachability test, so the orphan file currently satisfies its fail-closed `saw_containerapp` / `saw_zero_floor` checks. It passes for the wrong reason. |
| 3 | P2 | ops | ACA liveness/readiness probes against the already-exposed `/alive` and `/health`, via `ConfigureInfrastructure` in `src/SprintSync.AppHost/`; enable the stubbed Azure Monitor exporter in `ServiceDefaults/Extensions.cs:91-94` behind `APPLICATIONINSIGHTS_CONNECTION_STRING` and confirm traces arrive. | tasks/deploy | tasks.md **T057**. Verify against a real deploy — a wrong probe port crash-loops the container. |
| 4 | P1 | validation | Remaining half of T058: real `azd up`, the cross-tenant attack suite against the **deployed** API, and an interactive Entra External ID sign-in. Then close the "Not validated" gap in the T048 record. | implement | tasks.md **T058**. The "run CI live" half is **done** — see Verified baseline. |
| 5 | P2 | tooling | **New.** `.editorconfig` sets IDE0005 (unused usings) to `error`, but `Directory.Build.props` sets `GenerateDocumentationFile=false`, and IDE0005 does not run on build without it. The rule has never been enforced; the build says so via `EnableGenerateDocumentationFile` warnings. Decide: enable the doc file and fix the fallout, or drop the rule and stop implying it is enforced. | tasks | tasks.md **T059**. Surfaced by the first live CI run. The only item here that needs no Azure. |
| 6 | P2 | optional | Refresh provisioned `DisplayName` from the token on repeat login. | optional | tasks.md **T054**. Reviewer: no change required. Listed only so the deliberate skip stays visible. |

## Notes for the next session

- Items 1–4 all gate on one thing: someone running `azd up` against a real
  subscription with a real Entra External ID tenant. Nothing in 9B can be closed
  from a build environment. Sequence item 1 first — items 2 and 4 depend on its
  answer, and item 3 wants the same deploy to verify against.
- Item 5 is the only thing here that is workable without Azure. If a session has a
  .NET SDK, that is the one to pick up.
- Environment caveat that shaped the last session: the Claude Code container had no
  .NET SDK (`builds.dotnet.microsoft.com` is refused by the egress policy, 403 at
  the agent proxy) and no Docker. Phase 9A was therefore authored uncompiled and
  verified by CI on the PR. That worked, but expect the same constraint and plan for
  CI as the verification gate rather than local test runs.

## Consumption log

(none yet — this handoff is open)
