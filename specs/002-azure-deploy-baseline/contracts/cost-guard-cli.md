# Contract: Idle-cost guard CLI

**Feature**: 002-azure-deploy-baseline
**Implementations**: `scripts/check-idle-cost.sh`, `scripts/check-idle-cost.ps1`
**Consumers**: `azure.yaml` preprovision hook, CI `cost-guard` job, operators

Both implementations MUST satisfy this contract identically (FR-012). It is a
**safety mechanism**: a false pass costs money silently, so every ambiguity
resolves toward failing.

## Invocation

```
check-idle-cost.sh  [infra-path]     # default: <script-dir>/../infra
check-idle-cost.ps1 [-InfraPath ...]
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Every posture check passed, verified against templates that are actually deployed |
| `1` | One or more failures, each named on stdout |

## Deploy-path classification (NEW)

Every `.bicep` under the infra path is classified before any posture check runs.

| Path | Detection |
|---|---|
| `provision` | Transitively reachable from `main.bicep` via `module <name> '<relative-path>'` |
| `service-deploy` | Declares `Microsoft.App/containerApps` AND takes a container-image parameter |
| `hook` | Path appears in a hook command in `azure.yaml` |
| `none` | No rule matches |

**Rules**:
1. A template on **any** of the three paths is legitimate.
2. A template classified `none` is a **failure**, named explicitly.
3. Only classified templates may set `saw_containerapp` / `saw_zero_floor`.

> Reachability from `main.bicep` alone is **not** the test. Applying it as such
> fails two correct templates in this repository — the azd service-deploy module
> and the hook-deployed budget template (R6). A guard that fails every legitimate
> run gets disabled, which is worse than the bug being fixed.

## Posture checks (existing behaviour, now scoped to deployed templates)

| Check | Failure |
|---|---|
| No `minReplicas` ≥ 1 | `<file> : minReplicas is not 0` |
| A container app exists | `no Microsoft.App/containerApps resource found — cannot confirm scale-to-zero` |
| Something declares `minReplicas: 0` | `a container app is present but none declares minReplicas: 0` |
| SQL on free limit | `<file> : SQL database is not on the free limit` |
| SQL auto-pauses | `<file> : SQL database does not auto-pause when the free limit is spent` |
| A SQL database exists | `no Microsoft.Sql/servers/databases resource found — ...` |
| Registry on Basic | `<file> : container registry is not on the Basic SKU` |
| No idle-billable types | `<file> : <type> bills while idle` |
| **No unreachable template** | `<file> : on no deploy path — wire it in or delete it` |

## Fail-closed guarantees (preserved)

| Condition | Result |
|---|---|
| Infra path missing | Exit `1` |
| No `.bicep` files | Exit `1` |
| Container app absent | Exit `1` |
| SQL database absent | Exit `1` |
| Template on no path | Exit `1` |

A guard that passes because it found nothing to check is the failure mode this
design exists to prevent.

## Twin-parity requirement

Identical verdicts and identical failure sets on identical input. CI runs the
POSIX twin; the Windows twin runs on developer machines — divergence means a
developer's green is not CI's green.

## Verification fixtures

| Fixture | Expect |
|---|---|
| Real `infra/` | Exit `0` — **no false positive** on the service-deploy module or the budget template |
| Planted no-path `.bicep` declaring a container app | Exit `1`, template named |
| No-path template as the only `minReplicas: 0` source | Exit `1` — must not satisfy `saw_zero_floor` |
| Empty infra directory | Exit `1` |
| `minReplicas: 1` on a provision template | Exit `1` |
| Both twins, all above | Identical verdicts |
