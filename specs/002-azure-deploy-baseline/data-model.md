# Phase 1 Data Model: Azure Deployment & Operability Baseline

**Feature**: 002-azure-deploy-baseline | **Date**: 2026-08-22

This feature adds **no application entities** — no EF Core types, no migrations,
no DTOs. Feature 001's data model is unchanged and this feature must not alter
it.

What it does introduce are three non-persistent models: the configuration values
that must exist for a deploy to succeed, the classification the cost guard
applies to infrastructure templates, and the health states the platform reads.

---

## 1. Deployment configuration values

Environment-scoped configuration, held in the azd environment
(`.azure/<env>/.env`, gitignored). **Never committed** (FR-004).

| Value | Source | Consumed as | Validation |
|---|---|---|---|
| `AZURE_AZURE_AD_INSTANCE` | External ID tenant | `AzureAd__Instance` | Non-empty, no `REPLACE` placeholder |
| `AZURE_AZURE_AD_TENANT_ID` | External ID tenant | `AzureAd__TenantId` | as above |
| `AZURE_AZURE_AD_CLIENT_ID` | API app registration | `AzureAd__ClientId` | as above |
| `AZURE_AZURE_AD_AUDIENCE` | API app registration | `AzureAd__Audience` | as above |
| `BUDGET_ALERT_EMAILS` | Operator | Budget template parameter | JSON array; **absence skips the backstop rather than failing** (FR-015) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Provisioned by the AppHost | Gates the telemetry exporter | Absence must not prevent startup (FR-019) |

**Validation rules** (existing guard, `check-azuread-config.{sh,ps1}` — verified
correct, no change needed):
- All four `AZURE_AZURE_AD_*` present and non-empty, else provisioning is refused.
- None may contain `REPLACE` (placeholder detection).

**Known limitation** (spec edge case): the guard checks *presence and
placeholder-freedom*, not authenticity. Well-formed but wrong values pass the
guard and fail later at sign-in. The quickstart must make that failure
recognisable.

**Flow**: azd environment → `main.parameters.json` (`${AZURE_AZURE_AD_*}`) →
provisioning parameters → *unused at provision* → azd supplies the same values to
the service-deploy module at `azd deploy` → container app `AzureAd__*` env.

> The four `AzureAd*` parameters declared in `main.bicep` are **unused there**.
> This is azd-generated, reproduces on regeneration, and must not be "fixed"
> (spec, Settled Decisions). Identity reaches the API at deploy time.

---

## 2. Deploy-path classification

The model the cost guard applies to each `.bicep` under `infra/`. Purely
derived — computed per run, never stored.

### States

| Path | Detection | Counts toward posture checks |
|---|---|---|
| `provision` | Transitively reachable from `infra/main.bicep` via `module <name> '<relative-path>'` | Yes |
| `service-deploy` | Declares `Microsoft.App/containerApps` **and** takes a container-image parameter | Yes |
| `hook` | Path appears in a hook command in `azure.yaml` | Yes |
| **`none`** | Matches no rule above | **No — and fails the guard** |

### Current repository, classified

| Template | Path |
|---|---|
| `main.bicep` | `provision` (root) |
| `api-identity/api-identity.module.bicep` | `provision` |
| `api-roles-sql/api-roles-sql.module.bicep` | `provision` |
| `cae/cae.module.bicep` | `provision` |
| `cae-acr/cae-acr.module.bicep` | `provision` |
| `sql/sql.module.bicep` | `provision` |
| `api/api-containerapp.module.bicep` | **`service-deploy`** |
| `budget.bicep` | **`hook`** |

**Zero templates classify as `none`.** The guard must pass on this repository —
the last two rows are exactly the false positives a naive reachability test
produces (R6), and getting them right is the point of the change.

### Invariants

1. A template classified `none` fails the guard, named explicitly (FR-013).
2. `saw_containerapp` / `saw_zero_floor` are set **only** from templates on a
   path — presence under `infra/` proves nothing (FR-009).
3. Both twins reach identical verdicts on identical input (FR-012).
4. Fail-closed behaviour is preserved: if the resources to vet are absent
   entirely, the guard still fails.

---

## 3. Health states

Read by the platform, not persisted.

| State | Endpoint | Checks | Meaning |
|---|---|---|---|
| Live | `/alive` | tag `live` (`"self"`) | Process is responsive. Failure → restart. |
| Ready | `/ready` **(new)** | tag `ready` (startup gate) | Can serve requests. Failure → withhold traffic, do not restart. |
| Detailed | `/health` | all | Development only — **404 in Production**, deliberately (R2). |

### Startup gate — the one new readiness check

| Property | Value |
|---|---|
| Tag | `ready` |
| Initial state | Unhealthy |
| Becomes healthy | Once startup work (migration, seeding) completes |
| Reverts | Never — liveness handles a broken process |
| **Database access** | **None.** In-memory flag only. |

The database prohibition is not an optimisation. A readiness probe that opens a
connection every few seconds keeps the free-serverless database awake against its
60-second auto-pause, breaching the Deployment & Cost Constraints silently and
continuously (R3).

### Probe configuration

| Probe | Path | Notes |
|---|---|---|
| Liveness | `/alive` | Already exposed in all environments |
| Readiness | `/ready` | New. **Must not** target `/health` — 404s in Production, guaranteeing failure |

Declared in `AppHost.cs` via `PublishAsAzureContainerApp` (R4), never hand-written
into generated Bicep.

---

## 4. Validation record

The durable evidence FR-025 requires and 001's T048 record depends on.

| Field | Content |
|---|---|
| What ran | Which suite/scenario |
| Against what | Deployment URL, tenant, revision, commit |
| When | Date |
| Result | Pass/fail with observed output |
| Gap closed | Which 001 gap, if any |

**Destination**: 001's T048 validation record — the "Not validated" note there is
what this closes — plus this feature's own tasks.md.

**Retention**: permanent. It is the Principle IX evidence; a claim without it is
the state 001 is in today.
