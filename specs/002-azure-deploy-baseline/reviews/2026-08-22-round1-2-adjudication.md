# Committee review — PR #5 (feature 002), round 1–2

**Date**: 2026-08-22
**Panel**: SWE / Test Automation / QA / DevOps / Security + broker
**Method**: 3 rounds. Round 1 find (5 seats, independent) → round 2 adversarial
verify (one refuter per seat, instructed to default to REFUTED) → broker
adjudication. Round 3 re-reviews the fixes.
**Scope**: full PR diff, `main...002-azure-deploy-baseline` (56 files).

> **The QA seat failed mid-run** (API connection lost). This adjudication rests on
> **four** seats, not five. QA's lane — spec-vs-implementation conformance, edge
> cases, contract fidelity — is therefore the least-covered area of this review.
> Several findings below stray into it (M2, S4, N1 all concern contracts asserting
> what the code does not do), which suggests the lane was worth covering and may
> still hold more.

**Tally**: 38 findings raised, 5 refuted and dropped, 20 distinct defects survive
after deduplication into clusters.

---

## ADJUDICATION — PR #5, feature 002

**38 findings in. 5 refuted and dropped. 20 collapse into 7 clusters. 20 distinct defects survive.**

I spot-verified the load-bearing facts myself rather than trusting the dossier: `scripts/check-idle-cost.sh:113-215` (whole-file negative arm on workload profiles; basename fallback in `is_hook_deployed`; global `saw_zero_floor`), `src/SprintSync.Api/Program.cs:164-174` (`MarkReady()` at 172, `app.Run()` at 174), `azure.yaml:33-66` (`continueOnError: true` covering the SWA deploy, no `set -e`, trailing echo), `Extensions.cs:19-21/65-72` (`/ready` absent from the trace filter), `AppHost.cs:144-168` (liveness 5/30/3 vs readiness 3/5/30), and `grep -rn Cors src/ infra/` (no `Cors` anywhere in AppHost or the generated env array). All confirmed.

---

### DROPPED (refuted — do not re-raise)

| Seat | Finding | Why it dies |
|---|---|---|
| DevOps SWE-1 | `failureThreshold: 30` exceeds ACA limits | The "1-10" ceiling does not exist. ACA's own default readiness probe uses 48; the ARM schema declares no `maximum`. |
| DevOps SWE-9 | SWA hardcodes region/name | All three are `param`s with `@description`-documented defaults; region decoupling is the stated intent. No failure on the documented path. |
| SEC-5 | App Insights connection string as plain output | Reader on the RG can read it off the resource anyway; the output confers no incremental access. Remediation also forbidden (generated `infra/`). |
| SEC-6 | Unpinned `Azure/static-web-apps-deploy@v1` | `checkout@v4` and `setup-node@v4` float in the same job and run earlier, so pinning one action closes nothing. Repo-wide hardening decision, not a defect in this PR. |
| SEC-7 | `AllowCredentials()` on a Bearer API | Zero marginal privilege (no cookies exist, and the XSS path works identically without it), and the diagnosability half was empirically disproven — the wildcard case throws at host start with an explicit remediating message, which is *better* operator experience. |

---

### MUST FIX BEFORE MERGE

**M1 — Liveness probe kills the container ~65s into a startup window this same PR budgets at 153s.**
`AppHost.cs:151-153` → `infra/api/api-containerapp.module.bicep:61-69`. Probes fire at t=5/35/65; the third failure restarts. Nothing is listening until `app.Run()` (`Program.cs:174`), because migration + seed run as top-level code at `Program.cs:148-163`. `StartupMigrator.cs:40` waits up to 180 000 ms on `sp_getapplock`; `Program.cs:49` sets `CommandTimeout(60)` for a single command against a *resuming* serverless DB; `minReplicas: 0` makes a resuming DB the normal cold-start path. No `Startup` probe is emitted. **This PR introduces probes where ARM previously applied none** — `git show main:infra/api/api-containerapp.module.bicep | grep probes` is empty — so this is a regression this PR creates on the exact deploy path it exists to establish. The internal contradiction is decisive without a deploy: three in-repo budgets for the same pre-listen window (153s, 180s, 210s) all exceed 65s, and the *smallest* one is the one that kills. Fix: `ContainerAppProbeType.Startup` on `/alive` with a budget > 200s.
*(SWE-2 + DevOps-2 + SEC-2, all confirmed.)*

**M2 — The CORS allow-list this PR adds has no durable configuration path, and the documented remediation self-reverts.**
`Program.cs:67-69` reads `Cors:AllowedOrigins`. Nothing supplies it: no `builder.AddParameter("Cors…")` in `AppHost.cs` (contrast the four `AzureAd*` params at :82-85 wired at :99-102), no `Cors` key in `appsettings*.json`, no entry in the 16-item generated env array, no entry in `main.parameters.json`. Yet `azure.yaml:51`, `azure.yaml:66`, `deploy-web.yml:87` and `quickstart.md:187` all instruct the operator to set it by hand. `api-containerapp.module.bicep` is azd's deploy-time module (settled), so the next `azd deploy` PUTs `containers[0].env` wholesale and drops the out-of-band variable. Failure is silent and asymmetric: `curl` keeps returning 200, only the browser preflight breaks, and it gets blamed on the SPA. FR-030 has no durable implementation and no detector. Fix: add the parameter and change the docs to `azd env set … && azd deploy api`.
*(SEC-1, confirmed.)*

**M3 — `continueOnError: true` swallows a failed Static Web App provision, so `azd up` reports success with no SPA host.**
`azure.yaml:35`/`:54` were justified by the *optional* budget alert; the non-optional SWA deployment was appended into the same block (`:43-51`, `:62-66`). Two independent defects compound: `continueOnError` swallows the exit code, **and** the posix branch has no `set -e`, so the trailing `echo` becomes the hook's last command — it would report success even with `continueOnError` removed. The pwsh branch has the same shape. Operator sees "set the CORS origin to the hostname above" with no hostname above it; `deploy-web.yml` then has nothing to publish to.
*(DevOps-5, confirmed.)*

**M4 — The workload-profile rule is currently dead on the enforcing path, and the FR-012 parity claim is false today.**
`check-idle-cost.sh:176-179` applies the negative arm to the *whole file*, so one `Consumption` entry immunises every sibling in a `workloadProfiles` array. `check-idle-cost.ps1:169` uses a per-occurrence negative lookahead and catches it. `infra/cae/cae.module.bicep:52-57` **already declares a Consumption entry**, so on this repository's real tree the sh rule is permanently disabled — and `ci.yml:65` and `azure.yaml:22`'s posix hook both run the sh twin. Reproduced independently by three seats, including on a copy of the real tree with a `D4 / minimumCount: 1` profile appended: sh exits 0, ps exits 1. No fixture contains a `workloadProfileType` at all, so `run-tests.sh` structurally cannot see it. A dedicated ACA node is ~$150/mo standing — the single thing the guard exists to prevent.
*(SWE-1 + TA-3 + DevOps-3, all confirmed. This is the one I'd fix even if you deferred everything else in the guard, because it is a present-tense violation, not a latent one.)*

---

### SHOULD FIX

**S1 — Hook classification: whole-`azure.yaml` token scrape + basename fallback.** `sh:116` regexes the entire file (comments included) for `*.bicep`; `sh:124`/`ps1:112` fall back to bare basename equality. Reproduced three ways, most damningly: adding a *YAML comment* to `fixtures/orphan-containerapp` flips the suite's own must-FAIL case to PASS on both twins, and the same trick on `orphan-only-zero-floor` lets a never-deployed template satisfy the fail-closed scale-to-zero assertion (T052 defeated). Security's counter-check is what makes this cheap: **the basename fallback is load-bearing for nothing** — delete line 124 and the real repo still passes, `paid-static-web-app` still fails on the SKU, and the suite stays 18/18. One-line deletion plus restricting the scrape to hook `run:` scalars. *(SWE-4 + DevOps-8 + SEC-4.)*

**S2 — Posture rules are file-scoped, not resource-scoped; a compliant resource whitewashes a non-compliant sibling.** Three instantiations, all reproduced: (a) `saw_zero_floor` (`sh:159`) is a global flag set by *any* classified file containing the string `minReplicas: 0` — including inside a `//` comment — so a second container app with no scale block passes while the guard prints the affirmative false claim "container app scales to zero"; (b) a second `Microsoft.Sql/servers/databases` with `useFreeLimit: false` and `BC_Gen5_8` in the same generated `sql.module.bicep` passes both twins (reachable here — `sql.AddDatabase("reporting")` lands in that same file); (c) a Free + Standard SWA in one file defeats the `paid-static-web-app` fixture. Compounding: both twins only match *literal* minReplicas values, so `minReplicas: alwaysOnCount` is invisible — "I cannot read this value" must fail, not pass. Note the ACR sub-claim in DevOps-4 is mischaracterised (that rule is fail-on-presence, so file scoping makes it over-eager, not permissive); drop that clause. *(SWE-5 + TA-2 + DevOps-4.)*

**S3 — The suite cannot detect removal of the guard's most important rule.** Deleting the `minReplicas > 0` branch (`sh:160-162`) leaves `run-tests.sh` at 18/18, because `fixtures/min-replicas-one` is over-determined — the unrelated fail-closed clause at `sh:208-209` supplies the exit 1 on its own, and `run_case` compares nothing but exit codes. Same over-determination in `orphan-containerapp`, whose runner comment promises it "must FAIL and name the template" while nothing ever matches output. Fix: expected-substring assertions in `run_case`, plus a compliant second container app in the fixture. *(TA-4.)*

**S4 — All five `/ready` contract tests pass with zero readiness checks registered.** Deleting `.AddCheck<StartupGateHealthCheck>("startup", tags: ["ready"])` (`Extensions.cs:117`) leaves 17/17 green: an empty `HealthReport` aggregates to Healthy → 200, body `Healthy`, which also satisfies all three `DoesNotContain` leak assertions. `grep -rn '"/ready"' tests/` hits only that one file. This is the specific hole in the author's claim 2. *(TA-1.)*

**S5 — PowerShell twin reports every template as dead infrastructure on a relative `-InfraPath`.** `Get-NormalPath` (`ps1:72-74`) uses `[System.IO.Path]::GetFullPath`, which resolves against the .NET process CWD, while `Get-ChildItem`/`Resolve-Path -Relative` resolve against the PowerShell location; PS 5.1 does not sync them. Reproduced: `Set-Location <repo>; ./scripts/check-idle-cost.ps1 -InfraPath infra …` → exit 1 with all seven modules fabricated as dead. `contracts/cost-guard-cli.md:15` documents exactly that invocation. Loud and fail-closed, but the guard's own header argues that a guard failing legitimate runs gets disabled. One-line fix: `Convert-Path` / anchor the params. *(SWE-6.)*

**S6 — `/ready` is missing from the OpenTelemetry trace filter.** `Extensions.cs:69-71` excludes `/health` and `/alive` but not `ReadinessEndpointPath`, added in this PR at line 21. Readiness probes at `periodSeconds: 5` are six times the volume of the liveness probe that *is* excluded, and `UseAzureMonitor()` ships every span. ~720 probe spans/hour per warm replica, dominating App Insights transaction search. Plainly unintentional; one clause to fix. *(SWE-8.)*

---

### NOTED (fix opportunistically or hand off)

- **N1 — `MarkReady()` runs before Kestrel binds, so `/ready` can never report not-ready.** `Program.cs:172` vs `:174`. Four seats found this; three rate it P2 and I agree — during the pre-listen window ACA gets connection-refused, which withholds traffic exactly as a 503 would, so **there is no live operational failure**. What is defective is inert configuration plus three false assertions: `Program.cs:168-171`, `StartupGate.cs:10-13`, and `research.md` R3 all state the process is "listening but not yet servable", which it is not. `contracts/health-endpoints.md` Guarantee 1 ("Returns 503 until startup work completes") and its verification row are unsatisfiable as written. Fix the prose *or* move migration into an `IHostedService` — the latter also fixes M1. Compounding: TA moved `MarkReady()` above the migration block — the exact reorder its own comment forbids — and 67/67 stayed green, so the invariant is enforced by a comment's line number. *(SWE-3 + TA-5 + DevOps-6 + SEC-3.)*
- **N2 — `autoPauseDelay` is in minutes (60-minute floor), not seconds.** `AppHost.cs:69-70`'s comment is correct; `research.md` R3 ("one minute") and `StartupGate.cs:17` ("a 60-second auto-pause delay") are wrong. Separately, neither twin checks `autoPauseDelay` at all — `AutoPauseDelay = -1` would still print "SQL auto-pauses on the free limit". *(DevOps-7.)*
- **N3 — App Insights creates a second Log Analytics workspace; the AppHost comment says it reuses the CAE's.** `insights.module.bicep:8-19` vs `cae.module.bicep:30-39`. No standing cost (workspaces have no fixed fee), but neither sets `dailyQuotaGb`/`retentionInDays`, neither guard has any rule for `Microsoft.OperationalInsights/workspaces`, and app traces and container logs land in different workspaces. *(SWE-10 + DevOps-12.)*
- **N4 — Shell guard word-splits `find` output.** `sh:96` and `sh:148` iterate `$BICEP_FILES` unquoted; any path with a space shatters entries, fabricates nine failures, and — worse — never opens the real files, so `saw_containerapp`/`saw_sql` stay 0. Also a silent twin divergence (ps exits 0 on the same tree). Fails closed. Drop the sub-claim that `azd up` hits it on Windows — the hook routes Windows to pwsh. *(SWE-7 + TA-7.)*
- **N5 — `service-deploy` never cross-checks `azure.yaml` services**, so a stale azd per-service module is certified deployed forever. Small blast radius; posture checks still apply and the CI infra-drift job would likely flag the tree. *(SWE-9.)*
- **N6 — No `staticwebapp.config.json`**, so the new public origin serves no `frame-ancestors`, no `nosniff`, no CSP. The CSP half is conditional; the **framing half is unconditional** — clickjacking against the org-creation flow needs no second defect. `allowConfigFileUpdates: true` is already set; the fix is one file in `public/`. *(SEC-8.)*
- **N7 — `deploy-web.yml` validates three VITE_ secrets but not `AZURE_STATIC_WEB_APPS_API_TOKEN`**, and `staticwebapp.bicep:60-61`'s `output name` documents "the workflow that fetches the deployment token" — which does not exist anywhere. Undocumented manual step in the provision-to-publish chain. *(DevOps-10.)*
- **N8 — `/health`'s Development side is untested.** Deleting `MapHealthChecks(HealthEndpointPath)` from the Development branch keeps 67/67 green, so the contract's "Development only" is tested on one side. *(TA-6.)*
- **N9 — Node 20 ships, Node 24 validates.** `deploy-web.yml:46` vs `ci.yml:41-43`. No failure today (all installed engine ranges are satisfied by latest 20.x; jsdom's EBADENGINE is a warning with engine-strict off), but the release path is the one path CI never exercises. *(DevOps-11.)*
- **N10 — `run-tests.sh` writes `.sh.out`/`.ps.out` into git-tracked fixture dirs with no ignore rule**; an interrupted run leaves a dirty tree. Reproduced. Inputs stay hermetic. *(TA-8.)*

---

### VERDICT ON THE THREE CLAIMS

**Claim 1 — "the vacuous-pass bug is fixed."** Half right, and the half you asked about is the wrong half. The *specific* bug is genuinely fixed: `in_set` guards with `[ -n "$1" ] || return 1` and no fixture reaches it vacuously (three seats independently confirmed). But the answer to "are there OTHER inputs that make the guard pass vacuously" is **emphatically yes — at least four independent channels, three reproduced against the repo's own fixtures**: a YAML comment resurrects a must-FAIL orphan (S1); a global zero-floor flag lets one file's `minReplicas: 0` — even inside a `//` comment — vouch for a container app that was never read (S2); a non-literal `minReplicas: expr` is invisible (S2); a compliant sibling whitewashes a non-compliant one in the same file (S2). You fixed the bug you found and left the class it belongs to. The class is: *whole-file string presence is being used as evidence of resource-level properties.*

**Claim 2 — "/ready 200 + /health 404 are mutually confirming."** Sound for exactly one proposition and overclaimed for the one that matters. The pairing does rule out "the host is dead / everything 404s" and "the environment is not really Production" — that part holds, and no seat filed against it. It does **not** rescue `AndReportsReadyAfterStartup`, for two independent reasons: (a) deleting the entire readiness registration leaves all five tests green, because an empty `HealthReport` aggregates to Healthy → 200, so the 200 does not confirm that any readiness check exists (S4); (b) per N1 no not-ready state is HTTP-observable at all, so the "AfterStartup" clause has no contrasting case. Also note the environment-gate proof is itself one-sided — nothing asserts `/health` responds in Development, so deleting that mapping keeps everything green (N8). **Verdict: the pairing proves routing and environment. It proves nothing about readiness.**

**Claim 3 — "a DB-touching probe would defeat auto-pause; StartupGate avoids it."** The conclusion survives; the argument does not, and the gate is not meaningful readiness. `autoPauseDelay` is in **minutes** with a 60-minute floor, not 60 seconds — `AppHost.cs:70`'s comment is right, `research.md` R3 and `StartupGate.cs:17` are wrong. With a 60-minute pause window and ACA scale-to-zero (~5-minute cooldown), a replica only exists a few minutes past the last request, and while it exists the request traffic has already resumed the database — so a 5s probe cannot "keep the database permanently awake." Keeping readiness DB-free is still the right call (probe churn, dependency-blip flapping), just not for the stated reason. And the gate is not meaningful readiness: it can never be observed false over HTTP (N1), nothing tests the contract's "performs no database access" guarantee, nothing enforces the ordering its own comment declares MUST hold (moving `MarkReady()` above the migration block leaves 67/67 green), and no guard rule checks `autoPauseDelay` at all — `AutoPauseDelay = -1` would still print "SQL auto-pauses on the free limit."

---

### MERGE DECISION

**Not safe to merge as-is.** Four must-fixes, and none of them is a judgment call — all four were reproduced, and M1 and M4 are provable from the repository without a deploy.

**The single most important remaining risk is M1.** The liveness probe restarts the container roughly three times before the migration lock this PR designed is allowed to finish waiting, on a code path where nothing is listening on the port at all. This PR *introduces* probes where ARM previously applied none, so it is a regression created by the change, on the deploy path the change exists to establish — and the PR argues against itself in three places (153s readiness, 180s lock, 210s command timeout) while shipping a 65s kill budget. Worst case is a genuine crash loop on first deploy: the kill drops the connection, the session-scoped applock releases, and the restart re-runs the DDL from scratch. Best case it presents as "slow, flaky deploys," which is worse for diagnosis. The fix is one `ContainerAppProbeType.Startup` block in `AppHost.cs` plus a regen.

**Runner-up, and the one most likely to be dismissed as latent when it is not: M4.** The workload-profile rule is *already* dead on the enforcing twin because `cae.module.bicep` already carries a Consumption entry, and CI runs that twin. The FR-012/T053 parity assertion the PR advertises as a MUST is false today, not someday.

**Credit where it's due:** the design intent is unusually well documented, the three-deploy-path model is coherent, tenant isolation is untouched (`git diff --stat` confirms nothing under `Tenancy/` moved), the `UseCors`-before-`UseAuthentication` ordering is correct and creates no existence oracle, the fixture suite is input-hermetic, and 67/67 + 18/18 are green. The failure mode across this whole PR is a single repeated pattern: **comments and contracts asserting properties the mechanisms do not establish** — the guard's greps, the readiness gate's ordering, the App Insights workspace claim, the auto-pause units. Fix the four must-fixes; then, before the next feature, go back and make each of those sentences either true or gone.