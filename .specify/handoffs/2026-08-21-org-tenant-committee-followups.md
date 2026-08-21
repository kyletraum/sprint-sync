---
id: 2026-08-21-org-tenant-committee-followups
created: 2026-08-21
feature: 001-org-tenant-context
branch: 001-org-tenant-context
status: consumed
source: code-review committee (SWE / Test Automation / QA / DevOps + broker), 5 rounds
consume_in: [tasks, implement]
---

## Context

Feature 001 (Organization Onboarding & Tenant Context) was built via the full
SpecKit flow and then run through a 5-round multi-agent review committee. The
design was validated every round ("no isolation, data-loss, or security defect");
all genuine correctness bugs found — including regressions introduced under review
— were fixed and pushed (branch `393f1d7`, PR #2). What remains is work that could
not be verified in the build environment (no live Azure deploy, no real Entra
tenant, no CI runner) plus a few low-value refinements the committee marked
optional. These are the items to pick up on the next session, ideally around the
first real deploy.

## Verified baseline (known-good, do not re-verify from zero)

- Backend: **56/56** integration tests pass on real SQL (Testcontainers).
- Frontend: **5/5** vitest (incl. the FR-009 hide-existence UI assertion) + tsc + build.
- Cost guard + AzureAd config guard: both sh/ps1 twins, fail-closed, verified.
- `azd infra gen` synth verified: AzureAd flows as Bicep parameters, container app
  `minReplicas: 0`, SQL free serverless + AutoPause. NU1903 advisories explicitly
  suppressed (full audit still on).

## Decisions already made (do not re-litigate)

- Constitution v1.0.0, 9 named principles. Principle V interpretation: this slice
  has no query-filtered work-data entity yet, so isolation is enforced by the
  server-resolved, membership-verified tenant context + hide-existence; the global
  query-filter + write-guard infra is stood up and proven with a throwaway entity.
- Scoped (NON-pooled) DbContext so the query filter reads the correct per-request tenant.
- Hide-existence: uniform 404, timing-oracle-resistant by construction.
- `X-Organization-Id` = soft act-as hint: honored only when membership-verified,
  otherwise falls through to the active org.
- Org name: ≤100 UTF-16 code units; blank / whitespace / zero-width-only rejected.
- Cost posture: always-on, idle-optimized. Idle is a ~$5-8/mo floor (ACR Basic +
  Log Analytics), not $0; budget $20 (50% alert clears the floor).
- JIT provisioning is create-only (DisplayName not refreshed on later logins).
- Write-guard covers insert/update/delete against the ambient tenant.

## Outstanding items

| # | Priority | Area | Item | Suggested phase | Notes |
|---|----------|------|------|-----------------|-------|
| 1 | P0 | deploy | Confirm on a real `azd up` that the API container app actually provisions **and receives non-empty AzureAd__* env**. Resolve whether it deploys from `infra/main.bicep` or from the azd-applied module at `azd deploy`; if `infra/api/api-containerapp.module.bicep` is genuinely orphaned, wire it into `main.bicep` or delete it. | implement | Round 5 P0-1. An azd/Aspire deploy-model question, not a code defect; do NOT hand-wire main.bicep blind — it may conflict with azd's own deploy. |
| 2 | P1 | deploy/CI | After #1, extend `scripts/check-idle-cost.*` to reject orphan `Microsoft.App/containerApps` modules not reachable from `main.bicep`, so presence in `infra/` can't substitute for actual deployment. | tasks | Round 5 P1-3. |
| 3 | P1 | ops | Harden migrate-on-startup against the cross-revision rollout window: SQL app-lock (`sp_getapplock`) around `MigrateAsync`, or a one-shot pre-deploy migration step. Comment already corrected to acknowledge the window. | tasks | Round 5 P1-4. |
| 4 | P2 | ops | Add an ACA liveness/readiness probe targeting `/alive` and confirm OTLP export (enable the stubbed Azure.Monitor exporter behind `APPLICATIONINSIGHTS_CONNECTION_STRING`). | tasks/deploy | Rounds 2 & 5. Deferred because a wrong probe port crash-loops the container — verify against a real deploy. `/alive` is already exposed. |
| 5 | P2 | quality | Thread `CancellationToken` (`RequestAborted`) through the middleware and endpoint EF calls. | tasks | Round 5 P2-8. |
| 6 | P2 | quality | Deepen the OpenAPI file-vs-served contract test to assert declared response status codes and request-body required fields. | tasks | Round 5 P2-9. |
| 7 | P2 | style | Rewrite the tenant query-filter lambda to the idiomatic context-instance-member reference. Current reflection form works and is tested. | tasks | Round 5 P2-7. |
| 8 | P2 | optional | Refresh provisioned `DisplayName` from the token on repeat login (guarded to write only on change). | optional | Round 5 P2-13; reviewer: no change required. |
| 9 | P1 | validation | Run CI (`.github/workflows/ci.yml`) live and do a real `azd up`, then validate the cross-tenant attack suite + Entra sign-in end-to-end. None of this could be executed in the build environment. | implement | Whole-session gap, not a single finding. |

## Consumption log

### 2026-08-21 — consumed by `/speckit-tasks`

All 9 outstanding items were folded into
`specs/001-org-tenant-context/tasks.md` as **Phase 9: Handoff Follow-Ups —
Deploy Validation & Hardening**, split into 9A (buildable now) and 9B
(deploy-gated). Nothing was dropped. Mapping:

| Handoff item | Task | Phase |
|---|---|---|
| 3 — migrate-on-startup app-lock | T050 | 9A |
| 5 — thread `CancellationToken` | T051 | 9A |
| 6 — deepen OpenAPI contract test | T052 | 9A |
| 7 — idiomatic query-filter lambda | T053 | 9A |
| 8 — refresh `DisplayName` on repeat login | T054 (kept, marked OPTIONAL) | 9A |
| 1 — real `azd up`, AzureAd env, orphan container-app module | T055 (P0) | 9B |
| 2 — cost guard rejects unreachable containerApps modules | T056 (after T055) | 9B |
| 4 — ACA `/alive` probe + Azure Monitor OTLP export | T057 | 9B |
| 9 — live CI + real deploy + e2e attack/sign-in validation | T058 | 9B |

State confirmed while consuming (so the tasks name real code, not guesses):

- `infra/api/api-containerapp.module.bicep` is indeed **unreachable** from
  `infra/main.bicep` (which wires only api-identity, api-roles-sql, cae,
  cae-acr, sql) — item 1's premise holds.
- `scripts/check-idle-cost.sh` scans every `*.bicep` under `infra/` with no
  reachability test, so the orphan file currently satisfies its fail-closed
  `saw_containerapp` / `saw_zero_floor` checks — item 2's premise holds.
- `Program.cs:105-110` still carries the cross-revision CAVEAT comment; the
  `MigrateAsync` call runs under the execution strategy but no app-lock.
- `CancellationToken` appears in only 2 places under `src/` (DemoSeeder,
  AppDbContext override) — no middleware or endpoint threading yet.
- `AppDbContext.cs:120-133` still builds the filter via
  `Expression.Constant(this)` reflection.
- The Azure Monitor exporter in `ServiceDefaults/Extensions.cs:91-94` is still
  commented out; `/alive` and `/health` are exposed but no ACA probe targets them.

The "Decisions already made" section was carried into the Phase 9 preamble as
an explicit do-not-re-litigate note.

Follow-up: 9B stays open work in `tasks.md`, not in a handoff — it is now
task-shaped and tracked there.
