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
azd infra synth      # inspect generated Bicep BEFORE provisioning
#   confirm: api minReplicas = 0; SQL free serverless + AutoPause;
#   NO Microsoft.Cache/redis, NO dedicated workloadProfiles
azd up               # provision API+SQL (ACA/Azure SQL) and the SWA-hosted React app
```

Idle cost target ≈ $0 (scale-to-zero + SQL auto-pause + SWA Free); only the
container registry stands. Keep the $1 budget alert as a backstop (Principle 12).
