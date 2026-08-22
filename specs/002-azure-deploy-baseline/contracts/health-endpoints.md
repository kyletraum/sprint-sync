# Contract: Operational health endpoints

**Feature**: 002-azure-deploy-baseline | **Consumer**: Azure Container Apps probes

These are **operational** endpoints, deliberately outside the OpenAPI data
contract — consistent with the existing `/alive`. Principle III governs data
endpoints; these carry no tenant data and MUST NOT be added to `openapi.yaml`.

## `GET /alive` — liveness (existing, unchanged)

| | |
|---|---|
| Environments | All |
| Checks | Tag `live` (currently `"self"`) |
| Auth | None — probes are unauthenticated |
| `200` | Body `Healthy`. Process responsive. |
| `503` | Unhealthy. Platform restarts the container. |

Response body carries **no per-check detail**.

## `GET /ready` — readiness (NEW)

| | |
|---|---|
| Environments | **All**, Production included |
| Checks | Tag `ready` (startup gate) |
| Auth | None |
| `200` | Ready to serve. Platform may route traffic. |
| `503` | Not ready. Platform withholds traffic; **does not restart**. |

**Body**: status only — `Healthy` / `Unhealthy`. No per-check detail, no
exception text, no dependency names. This is what makes it safe in Production
where `/health` is not.

**Guarantees**:
1. Returns `503` until startup work (migration, seeding) completes.
2. Returns `200` thereafter for the process lifetime.
3. **Performs no database access.** A per-probe database connection would defeat
   the 60-second SQL auto-pause and breach the cost constraints (R3).
4. Never leaks per-check detail.

## `GET /health` — detailed (existing, unchanged)

| | |
|---|---|
| Environments | **Development only** |
| Production | **404** — deliberate, see https://aka.ms/aspire/healthchecks |

> **Do not point a probe at `/health`.** It 404s in Azure. A readiness probe
> aimed here fails every check, the revision never goes ready, and the deployment
> hangs or rolls back. This is the crash-loop the handoff warned about, and it is
> the reason `/ready` exists.

## Probe configuration contract

| Probe | Path | Port | On failure |
|---|---|---|---|
| Liveness | `/alive` | Container's HTTP port | Restart |
| Readiness | `/ready` | Container's HTTP port | Withhold traffic |

Declared in `AppHost.cs` via `PublishAsAzureContainerApp`. Port must track the
container's actual HTTP port — the generated module wires `targetPort` from
`api_containerport`; probes must not hardcode a different value.

## Verification

| Check | How |
|---|---|
| `/ready` exists in Production | `curl` the deployed URL; expect `200`, not `404` |
| Readiness gates traffic | Observe `503` during startup, `200` after |
| No database wakeup | Database reaches auto-paused state while a replica is alive |
| Platform calls them | ACA reports probe status; revision goes Ready |
| No detail leak | Response body is status only |
