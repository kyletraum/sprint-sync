# Implementation Plan: Organization Onboarding & Tenant Context

**Branch**: `001-org-tenant-context` | **Date**: 2026-07-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-org-tenant-context/spec.md`

## Summary

Stand up the multi-tenant walking skeleton: a signed-in user (authenticated by
Entra External ID) can create an organization and become its Owner, belong to
many organizations, list them, choose and switch an active organization, and is
structurally prevented from seeing or acting on any organization they are not a
member of. Isolation is enforced in one place — a server-resolved, membership-
verified tenant context feeding EF Core global query filters — never by
per-handler discipline. Non-member access is indistinguishable from
"not found." The React web app consumes the same public, versioned, OpenAPI-
documented API any third-party client would.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / React (frontend)

**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core 10 (Azure SQL
provider), .NET Aspire (orchestration), Microsoft.Identity.Web (Entra External
ID / CIAM), Asp.Versioning.Http, Microsoft.AspNetCore.OpenApi; React + Vite +
MSAL (frontend)

**Storage**: Azure SQL Database (shared schema, tenant discriminator). Local dev
uses a SQL Server container provisioned by Aspire. EF Core migrations from commit
one.

**Testing**: xUnit + `WebApplicationFactory` integration tests against the public
HTTP contract; SQL Server via Testcontainers (or Aspire test host) so tests run
against real migrations; a test authentication handler injects a configurable
authenticated identity (no live Entra dependency in tests). Frontend: Vitest +
React Testing Library (light, this slice).

**Target Platform**: Azure Container Apps (API, Consumption, scale-to-zero) +
Azure SQL free serverless offer (auto-pause) + Azure Static Web Apps Free
(React). Orchestrated via Aspire AppHost, deployed via `azd`.

**Project Type**: Web application (API backend + React frontend)

**Performance Goals**: Demo scale. Cold-start latency from ACA scale-to-zero and
SQL auto-pause is acceptable (per constitution Principle 12). No strict p95 SLA.

**Constraints**: Near-zero idle cost (scale-to-zero, SQL auto-pause, SWA Free);
no idle-billable dependencies (no Redis/RabbitMQ/always-on containers). Tenant
isolation must be structural, not by-convention.

**Scale/Scope**: Demo — tens of users, a handful of organizations per user; a
user may belong to many organizations but not thousands.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Status |
|---|-----------|------|--------|
| I | API as the Sole Data Path | React app calls only the public `/api/v1` endpoints; no DB access from the web tier. | ✅ PASS |
| II | Stable, Versioned Contract | Routes under `/api/v1`; every request/response is an explicit DTO in a `Contracts` namespace; EF entities are never serialized. | ✅ PASS |
| III | Documented Contract Conventions | OpenAPI document published at `/openapi/v1.json`; errors use `ProblemDetails` (RFC 9457); list endpoints use one offset-pagination envelope. | ✅ PASS |
| IV | Multi-Tenancy by Design | Shared schema; `Organization → Team → User` via Membership (Team deferred); tenant-scoped tables carry `OrganizationId` via a `TenantScopedEntity` base. | ✅ PASS |
| V | Isolation at the Persistence Layer | EF Core global query filter on `TenantScopedEntity` keyed to the ambient org; identity/control-plane tables (User, Organization, Membership) are the sanctioned carve-out; exactly one `IgnoreQueryFilters` path (org resolution). See interpretation note below. | ✅ PASS |
| VI | Server-Resolved, Verified Tenant Context | Middleware resolves the org from the user's persisted selection / request, **verifies it against Membership**, and sets the ambient value; a client-supplied id never sets context on its own. | ✅ PASS |
| VII | Identity and Authorization Are Separate | Entra External ID authenticates only; User/Membership/roles live in Azure SQL; JIT provisioning mirrors identity locally; tokens carry no authz state. | ✅ PASS |
| VIII | Unified, Policy-Based Authorization | One authorization policy layer (`OrgMember` requirement + handler); no ad-hoc role-string comparisons in endpoints. | ✅ PASS |
| IX | Contract-First, Isolation-Proven Testing | Integration tests against the public contract; cross-tenant attack tests (US4) and isolation tests are first-class. | ✅ PASS |
| — | Technology & Platform Constraints | Stack matches (.NET/Aspire/React/Azure SQL/EF migrations); AppHost is the topology source of truth. | ✅ PASS |
| — | Deployment & Cost Constraints | ACA scale-to-zero, SQL free serverless + auto-pause, SWA Free; no idle-billable deps; `azd infra synth` reviewed pre-deploy. | ✅ PASS |

**Interpretation note (Principle V) — no query-filtered entity exists yet.** This
foundational slice introduces no "work data" tables; the only entities are the
control-plane (User, Organization, Membership), which are *intentionally* outside
the global query filter (the carve-out). Isolation in this slice is therefore
enforced by the **server-resolved, membership-verified tenant context** (Principle
VI) that gates org-scoped access and returns "not found" for non-members —
enforced in one middleware/policy layer, not by per-handler discipline. The
global-query-filter infrastructure (`TenantScopedEntity` base + filter applied
from `ITenantContext`) is stood up now so that the first real tenant-scoped
entity (work items, a later feature) inherits isolation automatically and gets
its per-entity isolation test then. This is a deliberate, documented
interpretation — not a deferral of the guarantee — and no unjustified violations
exist, so **Complexity Tracking is empty**.

## Project Structure

### Documentation (this feature)

```text
specs/001-org-tenant-context/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── openapi.yaml
└── tasks.md             # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
SprintSync.sln
src/
├── SprintSync.AppHost/            # Aspire orchestration — topology source of truth
├── SprintSync.ServiceDefaults/    # Aspire shared telemetry/health/resilience defaults
└── SprintSync.Api/                # ASP.NET Core Minimal API — the ONLY data path
    ├── Contracts/                 # Request/response DTOs (the versioned wire contract)
    ├── Data/                      # AppDbContext, entities, global query filter, migrations
    │   ├── Entities/              # User, Organization, Membership, TenantScopedEntity (base)
    │   └── Migrations/
    ├── Tenancy/                   # ITenantContext, resolution middleware, IgnoreQueryFilters path
    ├── Auth/                      # Entra wiring, JIT user provisioning, OrgMember policy
    └── Features/                  # Endpoint groups: Me, Organizations
web/
└── sprint-sync-web/               # React + TypeScript + Vite (MSAL auth) — deploys to SWA Free
tests/
└── SprintSync.Api.Tests/          # xUnit integration tests against the public contract
```

**Structure Decision**: Web-application layout, kept deliberately lean — **three
.NET projects**, not the classic Domain/Application/Infrastructure/Api split.
Rationale: Principle II requires a DTO boundary (satisfied by the `Contracts/`
namespace vs `Data/Entities/`), *not* a project-per-layer; Principle V requires
query filters, *not* a repository pattern. EF Core `DbContext` is used directly in
thin endpoint handlers. Adding more projects or a repository abstraction would be
gratuitous complexity for a demo (and the plan template flags both as needing
justification — none exists, so we don't add them). The AppHost and
ServiceDefaults projects are Aspire requirements. The React app is a sibling
under `web/` and is referenced by the AppHost for local dev while deploying to
Static Web Apps.

## Complexity Tracking

> No Constitution Check violations. This table is intentionally empty.
