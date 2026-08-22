# Quickstart: Azure Deployment & Operability Baseline

**Feature**: 002-azure-deploy-baseline | **Date**: 2026-08-22

The operator path from nothing to a deployed, signed-in, isolation-proven Sprint
Sync — and the validation that proves each step worked. FR-002 and SC-002 require
this to be followable **without help from the implementers**, so it states where
things go wrong, not only the happy path.

> **Money starts at Step 4.** Steps 1-3 are free. Step 4 begins the ~$5-8/month
> idle floor (ACR Basic + Log Analytics) and it continues until the resources are
> deleted. See *Teardown*.

## Prerequisites

| Tool | Verify |
|---|---|
| Azure CLI, authenticated | `az account show` |
| azd ≥ 1.31.2 | `azd version` |
| .NET 10 SDK | `dotnet --version` |
| Node 20+ | `node --version` |
| An Azure subscription | `az account show --query state` → `Enabled` |

Both CLIs authenticate **separately** — azd does not read `az`'s credentials:

```powershell
az login --scope https://management.core.windows.net//.default
azd auth login
```

> Tokens expire after 90 days of inactivity. `AADSTS700082` or
> `Status_InteractionRequired` means re-authenticate — it does **not** mean the
> subscription is gone. `az login` reporting "No subscriptions found" after a
> failed tenant auth is a *consequence* of that failure, not evidence of a
> missing subscription; check `az account list --all`.

---

## Step 1 — Create the External ID tenant *(free)*

```powershell
az provider register -n Microsoft.AzureActiveDirectory
az provider show -n Microsoft.AzureActiveDirectory --query registrationState
```

Wait for `Registered` (typically a minute or two), then create the External ID
(CIAM) tenant via the Azure portal — *Microsoft Entra External ID* → create an
external tenant.

**Do not reuse "Default Directory."** It is the MSA-backed directory that owns
your subscription. Using it would have customers signing in to the directory that
owns your billing account.

**Verify**: the new tenant appears with its own tenant ID, distinct from the
subscription's `homeTenantId`.

---

## Step 2 — Register the applications *(free)*

In the **External ID tenant** (not the subscription's directory), register the
API and the web app.

Collect four values:

| Value | Where |
|---|---|
| `AZURE_AZURE_AD_INSTANCE` | Tenant authentication endpoint |
| `AZURE_AZURE_AD_TENANT_ID` | The External ID tenant's ID |
| `AZURE_AZURE_AD_CLIENT_ID` | API app registration → Application (client) ID |
| `AZURE_AZURE_AD_AUDIENCE` | API app registration → Application ID URI / audience |

```powershell
azd env set AZURE_AZURE_AD_INSTANCE   "<...>"
azd env set AZURE_AZURE_AD_TENANT_ID  "<...>"
azd env set AZURE_AZURE_AD_CLIENT_ID  "<...>"
azd env set AZURE_AZURE_AD_AUDIENCE   "<...>"
```

Set the budget backstop **now**, before deploying — the postprovision hook skips
silently when it is unset, and a skipped cost backstop is discovered on a bill:

```powershell
azd env set BUDGET_ALERT_EMAILS '["you@example.com"]'
```

**Verify**:

```powershell
./scripts/check-azuread-config.ps1     # expect: all four values set
```

> This guard checks **presence and placeholder-freedom, not authenticity**.
> Well-formed but wrong values pass here and fail later at sign-in with a token
> validation error. If Step 6 fails to authenticate, suspect these values first.

---

## Step 3 — Verify before spending *(free — last free step)*

```powershell
azd infra gen --force
git diff --exit-code infra/      # expect: no diff
./scripts/check-idle-cost.ps1    # expect: exit 0
dotnet test                      # expect: all green
```

**Expected**: infra regenerates identically, the cost guard passes on all eight
templates (six provision, one service-deploy, one hook), and no template
classifies as `none`.

> If the cost guard fails naming `api/api-containerapp.module.bicep` or
> `budget.bicep`, the three-path classification is wrong. Those are legitimately
> deployed — the first by azd at deploy time, the second by the postprovision
> hook. **Neither should be wired into `main.bicep`, and neither should be
> deleted.**

---

## Step 4 — Deploy *(spend begins)*

```powershell
azd up
```

The preprovision hooks run the config guard then the cost guard; both fail
closed. Provisioning does not start until both pass.

**Verify**:

```powershell
azd env get-values | Select-String "SERVICE_|AZURE_"
az containerapp show -n api -g rg-<env> --query "properties.runningStatus"
```

Confirm the four `AzureAd__*` values are present and **non-empty** in the
container app's environment. Empty values here mean the deployment failed even if
`azd up` reported success (FR-006).

**Common failures**:

| Symptom | Cause |
|---|---|
| Revision never goes Ready | Probe misconfiguration — verify readiness targets `/ready`, not `/health` (which 404s in Production) |
| Container crash-loops at startup | The API's fail-fast guard rejecting missing/invalid `AzureAd__*` |
| Hook skipped the budget | `BUDGET_ALERT_EMAILS` unset — see Step 2 |

---

## Step 5 — Verify operability

```powershell
curl https://<api-url>/alive     # expect 200
curl https://<api-url>/ready     # expect 200
curl https://<api-url>/health    # expect 404 — correct in Production
```

`/health` returning 404 is **success**, not a defect.

**Traces**: exercise the API, then query Application Insights for a request
trace. Expect it within 5 minutes (SC-006).

**Auto-pause**: leave the system idle and confirm the database reaches its paused
state while a replica may still be alive. If it never pauses, the readiness probe
is touching the database — a cost breach (R3).

---

## Step 6 — Sign in

Open the deployed web app, sign in with an External ID account.

**Expect**: authenticated, provisioned on first sign-in, own organization
visible. This is the walkthrough feature 001's T048 record lists as **"Not
validated"** — completing it is what closes that gap.

---

## Step 7 — Prove isolation *(the Principle IX gate)*

Two users in two different organizations. Confirm against the **deployed** API:

1. A member of org A cannot read org B's resources.
2. A non-existent resource and another org's resource are **indistinguishable**.
3. Asserting membership of a foreign organization is ignored.

Record what ran, against which deployment and commit, when, and the result — into
001's T048 validation record.

> Until this passes, **neither feature is production-done.** Feature 001's
> isolation guarantees remain proven only against Testcontainers.

---

## Teardown

```powershell
azd down --purge
```

Stops all spend. `--purge` matters: soft-deleted resources can otherwise keep
billing or block re-creating names.

---

## Validation summary

| Step | Proves | Criteria |
|---|---|---|
| 1-2 | Identity exists, config passes | US1 sc.1 |
| 3 | Infra correct before spending | US4 |
| 4 | Deploys with real config | US1 sc.2-3, FR-006 |
| 5 | Platform can see health; traces queryable | US3, SC-006 |
| 6 | A real person can sign in | US1 sc.4, SC-001 |
| 7 | **Isolation holds when deployed** | US2, SC-003, SC-009 |
