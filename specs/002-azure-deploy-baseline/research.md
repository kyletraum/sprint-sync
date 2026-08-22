# Phase 0 Research: Azure Deployment & Operability Baseline

**Feature**: 002-azure-deploy-baseline | **Date**: 2026-08-22

All findings below were verified against the repository and the live Azure
subscription on 2026-08-22. Items the spec marks **Settled** are not revisited
here; this document resolves what the plan still needed to know.

---

## R1: Can an Entra External ID tenant be created on this subscription?

**Decision**: Yes. Proceed with tenant creation as the first task.

**Rationale**: The whole feature rests on this and it was the spec's one open
risk. Checked directly:

```
az provider show -n Microsoft.AzureActiveDirectory
  → state: NotRegistered
  → resourceTypes includes: ciamDirectories, entraTenants, b2cDirectories
```

`ciamDirectories` is the External ID (CIAM) tenant type, and it **is offered on
this subscription**. `NotRegistered` is the default state for any provider never
used — it is a one-command fix (`az provider register -n
Microsoft.AzureActiveDirectory`), not a capability limitation or an entitlement
problem.

The concern that the personal-MSA subscription might not support External ID is
therefore **not** borne out. The subscription is `Enabled` and offers the
resource type.

**Alternatives considered**:
- *Reuse the existing "Default Directory"*: rejected. It is the MSA-backed
  directory owning the subscription, not a customer identity tenant. Using it
  would mean customers sign in to the directory that owns the billing account.
- *Azure AD B2C* (`b2cDirectories`, also available): rejected. It is the
  predecessor to External ID and is in maintenance; feature 001 was built and
  documented against External ID throughout.

**Residual risk**: registration succeeds but tenant *creation* hits a quota or a
region restriction. Cheap to discover — it is the first task, and it fails loudly
before any spend.

---

## R2: What should the readiness probe target? (`/health` is Development-only)

**Decision**: Add a new `/ready` endpoint mapped in **all** environments,
filtered to health checks tagged `ready`, with no detail in the response body.
Point the ACA readiness probe at it. Point liveness at the existing `/alive`.

**Rationale**: This is the single most dangerous detail in the feature, and the
handoff's warning ("a wrong probe port crash-loops the container") understated
it. Read the code:

```
ServiceDefaults/Extensions.cs:109-126
  /alive   → mapped always, filtered to tag "live"
  /health  → mapped ONLY when app.Environment.IsDevelopment()
```

In Azure the app runs in Production. **`/health` returns 404 there.** A readiness
probe aimed at `/health` — exactly what the handoff and the spec's inherited
phrasing suggest — would fail every probe, ACA would never mark the revision
ready, and the deployment would roll back or hang. The task would have looked
like a one-line change and taken the deployment down.

`/health` is Development-only for a documented security reason (it returns
per-check detail; see https://aka.ms/aspire/healthchecks). That reason is sound
and is **not** being overturned: `/ready` returns status only, no per-check
detail, so it is safe in Production while `/health` stays as it is.

**Alternatives considered**:
- *Point readiness at `/alive` too*: rejected. Liveness and readiness would be
  identical, so readiness gates nothing and FR-017 is unmet in substance while
  appearing green.
- *Map `/health` in Production*: rejected. Overturns a deliberate security
  decision to save one endpoint.
- *Omit the readiness probe*: rejected — Principle X requires it.

---

## R3: What should the readiness check actually check? (cost tension)

**Decision**: A **database-free** readiness gate that reports unready until
application startup work completes, then healthy. Register it tagged `ready`.

**Rationale**: This is a direct collision between two constitution obligations,
and it must be resolved in the design rather than discovered in the bill.

The obvious readiness check is database connectivity. It is also **forbidden
here**: the database is Azure SQL free serverless with `AutoPauseDelay = 60`
(one minute). A readiness probe that opens a database connection every few
seconds would keep the database permanently awake, defeating auto-pause and
breaking the Deployment & Cost Constraints. Principle X, implemented naively,
would silently dismantle the cost posture the constitution is most explicit
about.

A startup gate is both cheap and more truthful. The API runs migrations at
startup (`Database__MigrateOnStartup=true`, and feature 001 added an app-lock
around it). During that window the process is listening but cannot serve —
precisely the state readiness exists to describe. The gate reads an in-memory
flag set once when startup work completes: no database traffic per probe, and it
correctly withholds traffic during migration.

**Alternatives considered**:
- *EF Core `AddDbContextCheck`*: rejected — defeats auto-pause, as above.
- *Cached database check (e.g. 5-minute TTL)*: rejected. Still touches the
  database on a timer, and adds a staleness window during which readiness lies.
- *Readiness identical to liveness*: rejected — see R2.

**Note**: only one health check exists today (`"self"`, tagged `live`). There is
no `ready`-tagged check at all, so this is new registration, not a re-tag.

---

## R4: Where do the probes get wired?

**Decision**: In `AppHost.cs`'s existing `api.PublishAsAzureContainerApp(...)`
callback.

**Rationale**: The handoff and the spec both say `ConfigureInfrastructure`. That
is **wrong for this resource**. `ConfigureInfrastructure` is used in this repo on
the SQL resource (`AppHost.cs:31`); the API container app is customised through
`PublishAsAzureContainerApp` (`AppHost.cs:83-89`), which already sets
`Template.Scale.MinReplicas/MaxReplicas`. Probes belong on the same container
template, in the same callback.

This also satisfies the Technology & Platform Constraint that the AppHost is the
single source of truth for topology — probes must not be hand-written into the
generated Bicep, which regenerates.

---

## R5: How does telemetry reach a queryable destination?

**Decision**: Add an Application Insights resource to the AppHost in publish
mode, reference it from the API so the connection string is injected, and enable
the already-stubbed Azure Monitor exporter behind that connection string.

**Rationale**: `ServiceDefaults/Extensions.cs:91-94` has the exporter commented
out with the note that it "requires the Azure.Monitor.OpenTelemetry.AspNetCore
package" — the package is genuinely absent from the `.csproj`, so this is a
package addition plus an un-stubbing, not just uncommenting.

The exporter is gated on `APPLICATIONINSIGHTS_CONNECTION_STRING`, which nothing
currently supplies — the infrastructure has no Application Insights component.
The `cae` module does create a Log Analytics workspace
(`Microsoft.OperationalInsights/workspaces`, `cae.module.bicep:30`), which is the
right backing store; workspace-based Application Insights writes into a
workspace like that one.

Declaring it in the AppHost (rather than hand-editing Bicep) keeps topology in
one place and makes the connection string flow to the container app the same way
the other settings do.

**Cost analysis** — required, because this adds infrastructure:
- Application Insights (workspace-based) has **no standing resource fee**. It
  bills on data ingested into the workspace it targets.
- Log Analytics is already an accepted component of the ~$5-8/month idle floor.
- At idle the app scales to zero and emits nothing, so idle ingestion is ~zero.
- A free monthly ingestion grant covers demo-scale traffic.

**Conclusion**: no new *standing* cost, consistent with "the only permitted
standing cost is the container registry". This must still be confirmed against
observed spend (SC-004) rather than asserted — sampling should be configured if
ingestion proves non-trivial.

**Alternatives considered**:
- *Console logs into Log Analytics only* (already happens via ACA): rejected.
  FR-018 requires **traces** to be queryable; container stdout is not traces.
- *Self-hosted OTLP collector*: rejected outright — an always-on dependency, the
  exact thing the constitution forbids.

---

## R6: How should the cost guard classify deploy paths?

**Decision**: Classify every `.bicep` under `infra/` into one of three paths,
count only classified templates toward the posture checks, and fail on any
template matching none.

The three paths, per the constitution's revised Pre-deploy verification rule:

| Path | Detection |
|---|---|
| **Provision** | Transitively reachable from `infra/main.bicep` by `module <name> '<relative path>'` declarations |
| **Service deploy** | The azd per-service module — declares a container app **and** takes a container-image parameter, which cannot exist at provision time |
| **Declared hook** | Path appears in a hook command in `azure.yaml` |

**Rationale**: A naive "reachable from main.bicep" test produces **two false
positives on the current, correct repository** — this was verified, and it is why
the constitution amendment was rewritten mid-session:

- `infra/api/api-containerapp.module.bicep` — the azd service-deploy module.
- `infra/budget.bicep` — deployed by the `azure.yaml` postprovision hook
  (`az deployment group create -f infra/budget.bicep`).

A guard that failed on both would be worse than the current one: it would fail
every legitimate run and be disabled within a week.

The image-parameter test for path 2 is the same evidence that settled T055: the
module takes `api_containerimage`, which is what makes it a deploy-time artifact.
It is a structural property, not a filename convention, so it survives azd
regenerating the file.

**Consequence for the posture checks**: `saw_containerapp` and `saw_zero_floor`
must be set only from templates on a path. Today the orphan-looking service
module satisfies both — which is correct by luck, since it *is* deployed, but the
guard has no way to know that. After this change it is correct by construction.

**Alternatives considered**:
- *Allowlist the two known files*: rejected. Silently re-breaks the moment azd
  emits a differently-named module.
- *Parse with a Bicep toolchain*: rejected. Adds a build dependency to a guard
  whose whole value is running early and everywhere; the module-reference grammar
  is simple enough to match directly.

---

## R7: How is the attack suite run against a deployed API?

**Decision**: Determined during implementation, after the deployment exists.
Recorded here as an explicit open item rather than a guess.

**Rationale**: `tests/SprintSync.Api.Tests/Isolation/` contains three files
(`CrossTenantAccessTests`, `QueryFilterTests`, `TenantWriteGuardTests`). They are
integration tests built on the in-process test host with Testcontainers SQL.
Retargeting them at a deployed URL is not a configuration flip:

- They need real tokens from the External ID tenant for two distinct users in
  two distinct organizations. In-process they can forge identity; against a
  deployed API they cannot.
- `QueryFilterTests` and `TenantWriteGuardTests` assert on **persistence-layer**
  behaviour (the global query filter, the write guard) via a throwaway entity.
  Those are white-box by design and are **not** meaningfully re-runnable against
  a black-box public API.

So FR-022's "the cross-tenant attack suite" realistically means
**`CrossTenantAccessTests`** — the public-contract cases — re-expressed against
the deployed endpoint, not all three files lifted wholesale. The other two remain
valuable exactly where they are.

**This must be settled before the tasks for User Story 2 are written.** Deciding
it now, without a deployment or tokens in hand, would be guessing. Flagged in
plan.md as the one deliberate deferral.

---

## R8: How is "CI can fail" demonstrated?

**Decision**: Open a throwaway pull request containing one deliberately broken
test, confirm CI goes red, then close it without merging.

**Rationale**: FR-024 requires the pipeline be shown to fail. Run 32537061874
proves it runs and passes; nothing proves it can go red. A pipeline that cannot
fail is indistinguishable from one that always passes.

A throwaway PR is the honest test: it exercises the real trigger path on the real
runner. Asserting it locally proves nothing about the workflow.

**Alternatives considered**:
- *Trust the green run*: rejected — that is the assumption under test.
- *Temporarily break a test on the feature branch*: rejected. Pollutes feature
  history and risks the break being merged.

---

## R9: Stale constitution references in code comments

**Finding, not a decision**: `AppHost.cs:3` reads *"the single source of truth for
service topology (Principle X)"*, and several comments cite "Principle 12". Both
predate constitution v1.0.0's renumbering — topology is a Technology & Platform
Constraint, and the old #12 is now Deployment & Cost Constraints.

This was harmless until 2026-08-22, when v1.1.0 introduced a **real Principle X
(Operable by Default)**. `AppHost.cs` now cites "Principle X" to mean something
Principle X does not say, in the very file this feature edits to implement
Principle X.

Low-cost correction, worth taking while editing the file. Not a functional
defect; no behaviour depends on a comment.

---

## Summary of unknowns resolved

| # | Unknown | Resolved |
|---|---|---|
| R1 | External ID tenant feasible on this subscription? | Yes — `ciamDirectories` offered; provider needs registering |
| R2 | Readiness probe target | New `/ready`, mapped in all environments, status-only |
| R3 | What readiness checks | DB-free startup gate — a DB check would defeat SQL auto-pause |
| R4 | Probe wiring location | `PublishAsAzureContainerApp`, **not** `ConfigureInfrastructure` |
| R5 | Telemetry destination | App Insights via AppHost; no new standing cost |
| R6 | Deploy-path classification | Three-path test; naive reachability yields 2 false positives |
| R7 | Attack suite against deployed API | **Deferred** — decide after deployment exists |
| R8 | Prove CI can fail | Throwaway PR with a broken test |
| R9 | Stale "Principle X" comment | Correct while editing `AppHost.cs` |
