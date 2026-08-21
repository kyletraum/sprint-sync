# Quickstart: Organization Onboarding & Tenant Context

A run/validate guide proving feature 001 works end-to-end. Implementation detail
lives in [data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml),
and (once generated) `tasks.md` — this file is about *running and checking*.

## Prerequisites

- .NET 10 SDK, Node 20+, Docker (for the local SQL container / Testcontainers)
- .NET Aspire workload, `azd` (deploy only)
- An Entra External ID (CIAM) tenant with a user flow + app registration
  (deploy/live only — integration tests do **not** need it)

## Run locally

```bash
# From repo root — Aspire AppHost is the single source of truth for topology
dotnet run --project src/SprintSync.AppHost
```

Aspire starts: SQL Server (container), the API (migrations applied on startup),
and the React app. Open the Aspire dashboard URL it prints; from there follow the
`web` endpoint for the UI and the `api` endpoint for `/openapi/v1.json`.

## Validate the contract is published (Principle III)

```bash
curl -s http://localhost:<api-port>/openapi/v1.json | jq '.info.version, (.paths | keys)'
# Expect "1.0" and the five paths from contracts/openapi.yaml
```

## Validate the user journeys

> Use the web UI, or drive the API directly with a bearer token. Locally, the
> test auth handler / a dev token stands in for Entra.

| Scenario | Steps | Expected (spec ref) |
|----------|-------|---------------------|
| First sign-in (JIT, empty state) | Sign in as a brand-new user → `GET /me` | 200, `activeOrganizationId: null`; UI shows "create your first organization" (FR-013, FR-005 edge) |
| Create org & become Owner | `POST /organizations {"name":"Acme"}` | 201, `role: Owner`; becomes active org (US1, FR-001/002) |
| See only my orgs | Create "Acme" and "Beta"; `GET /organizations` | Both returned, `role: Owner`; a third user's org never appears (US2, SC-003) |
| Switch active org | `PUT /me/active-organization {"organizationId": <Beta>}` | 200, `activeOrganizationId` = Beta; next request scoped to Beta (US3) |
| Reject empty name | `POST /organizations {"name":"  "}` | 400 ProblemDetails; nothing created (FR-012) |

## Validate isolation — the safety-critical checks (Principle IX, US4)

Set up two users: **A** (member of "Acme") and **B** (member of "Gamma").

| Attack | Steps (as A) | Expected |
|--------|--------------|----------|
| Read a non-member org | `GET /organizations/{GammaId}` | **404**, ProblemDetails identical to a random nonexistent id — no signal that Gamma exists (FR-008/009, SC-002) |
| Enumerate by probing | `GET /organizations/{randomGuid}` vs the Gamma call | Responses indistinguishable (status, body, ~timing) |
| Hijack active org | `PUT /me/active-organization {"organizationId": <GammaId>}` | **404**; A's active org unchanged (FR-007) |
| Client-supplied org id | Send `X-Organization-Id: <GammaId>` on any scoped call | Ignored for authorization; context resolves from A's verified membership only (FR-010) |
| Stale membership | Remove A's Acme membership (test-only), then `GET /organizations/{AcmeId}` | **404**; access stops immediately, no session-cached grant (edge case) |

## Automated verification

```bash
dotnet test tests/SprintSync.Api.Tests
```

The suite runs against the public HTTP contract on a real SQL container and
includes the cross-tenant attack class above plus the query-filter mechanism
proof (research R2/R9). All isolation checks are part of the permanent suite, not
manual steps.

## Deploy (optional, always-on idle-optimized)

```bash
azd infra gen        # generate the Bicep BEFORE provisioning
./scripts/check-idle-cost.sh          # or: pwsh ./scripts/check-idle-cost.ps1
azd up               # provision API + SQL (ACA / Azure SQL). The React app is
                     # deployed to Static Web Apps Free from CI, not azd (P1-4).
```

### Required azd env values

Set these before `azd up` — the API **fails fast at startup** in the cloud if the
Entra config is missing, rather than 401-ing every request (P0-1):

```bash
azd env set AZURE_AZURE_AD_INSTANCE   "https://<your-ciam>.ciamlogin.com/"
azd env set AZURE_AZURE_AD_TENANT_ID  "<tenant-id>"
azd env set AZURE_AZURE_AD_CLIENT_ID  "<api-app-registration-client-id>"
azd env set AZURE_AZURE_AD_AUDIENCE   "api://<api-app-registration-client-id>"
azd env set BUDGET_ALERT_EMAILS '["you@example.com"]'   # optional; enables the cost backstop
```

The AppHost exposes these as Bicep **parameters** that azd resolves into the
container app's `AzureAd__*` env (so real values deploy, never empty literals).
A **preprovision guard** (`scripts/check-azuread-config`) refuses to provision
unless **all four** are set and placeholder-free, so a misconfigured deploy fails
before any resource is created — not as a crash-looping revision (P1-4/P1-5). The
budget alert is provisioned by the postprovision hook when `BUDGET_ALERT_EMAILS`
is set.

### The pre-deploy cost guard (T045, Principle 12)

`scripts/check-idle-cost.{sh,ps1}` is the mechanical form of "nothing idles
billably". It reads the synthesized Bicep and fails the deploy if:

- any Container App has `minReplicas` > 0,
- the SQL database is not on the free serverless offer (`useFreeLimit: true`)
  with `freeLimitExhaustionBehavior: 'AutoPause'`,
- an always-on resource type appears (Redis, Service Bus, Cosmos, PostgreSQL
  flexible server, AKS),
- a non-Consumption ACA workload profile is used, or
- the container registry is not on the Basic SKU (P1-5).

It is wired into `azure.yaml` as an `azd` **preprovision hook**, so it runs on
every `azd up` whether or not anyone remembers to run it by hand. Both scripts
take an optional path argument (default `./infra`).

### Cost backstop

A **$20/month budget** (alerting at 50% actual, 100% actual, 100% forecast) is
provisioned automatically by the azd postprovision hook when `BUDGET_ALERT_EMAILS`
is set (P1-6) — no manual `az deployment`. The ceiling sits above the unavoidable
~$5-8/mo floor (below), so the 50% alert ($10) signals a genuine anomaly rather
than the expected registry bill (P1-5).

### Infrastructure generation

`azd infra gen` succeeds: the Aspire AppHost is the only azd service (azure.yaml
declares no sibling — that pairing was the earlier synth failure, now fixed).
The generated Bicep places the API container app at `minReplicas: 0` and the SQL
database on the free serverless offer with `AutoPause`. The cost guard passes on
that output and fails closed when those resources are absent, so a
misconfiguration cannot slip through as a green check.

**Idle cost is a ~$5-8/month floor**, not $0: the container registry (ACR Basic)
plus a little Log Analytics stand regardless, while the API scales to zero, SQL
auto-pauses, and the SPA sits on Static Web Apps Free. The cost guard keeps ACR on
the Basic SKU so that floor cannot silently grow (P1-5).

### Known acceptances

- **SQL public network access** is enabled (the free serverless database is
  reachable over the internet, firewalled). A conscious acceptance for a demo;
  the hardening path is a private endpoint, or scoping the firewall to the
  container app environment's outbound IP (P2-12).
