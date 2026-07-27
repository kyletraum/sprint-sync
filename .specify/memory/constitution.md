<!--
SYNC IMPACT REPORT
==================
Version change: (none) -> 1.0.0  (initial ratification)
Rationale: First adoption of the Sprint Sync constitution, restructured from the
original 12-item draft into named, gate-referenceable principles.

Modeled from the original draft:
  - Original #1            -> Principle I  (API as the Sole Data Path)
  - Original #2            -> Principle II (Stable, Versioned Contract)
  - Original #3 + new gaps -> Principle III (Documented Contract Conventions:
                              OpenAPI + ProblemDetails + pagination)
  - Original #4            -> Principle IV (Multi-Tenancy by Design)
  - Original #5            -> Principle V  (Isolation at the Persistence Layer)
                              + explicit identity/membership cross-tenant carve-out
  - Original #6            -> Principle VI (Server-Resolved, Verified Tenant Context)
  - Original #7            -> Principle VII (Identity and Authorization Are Separate)
  - Original #8            -> Principle VIII (Unified, Policy-Based Authorization)
  - Original #5 + #11      -> Principle IX (Contract-First, Isolation-Proven Testing)
  - Original #9            -> Technology & Platform Constraints
  - Original #10           -> Technology & Platform Constraints
  - Original #12           -> Deployment & Cost Constraints (rewritten for the
                              always-on, idle-optimized posture)

Naming resolution: the person entity is "User"; their link to an org/team is a
"Membership"; "Member" now denotes ONLY the org-level role.

Templates / follow-ups:
  - TODO: run SpecKit `specify init --here` and reconcile command templates.
  - Constitution Check gate references principle names below (I-IX).
-->

# Sprint Sync Constitution

Sprint Sync is a multi-tenant work item tracker shipped as two client-facing
products: a public HTTP API and a React web app. This constitution defines the
non-negotiable invariants every feature spec, plan, and task is validated
against. Where a principle uses MUST / MUST NOT, deviation is a defect, not a
preference.

## Core Principles

### I. API as the Sole Data Path
The public HTTP API is the only path to data. The React web app consumes the
same public endpoints any third-party client would — there are no private
routes and no direct database access from the web tier.
**Rationale:** One contract, one security surface, one thing to document and
test. A private back channel is untested, undocumented, and the first place
isolation guarantees rot.

### II. Stable, Versioned Contract
The API contract is versioned and stable. Breaking changes require a new
version; they are never made in place. EF Core entities MUST NOT be serialized
directly — every request and response crosses an explicit DTO boundary.
**Rationale:** DTOs decouple the wire contract from the persistence model,
prevent accidental over-exposure of internal/tenant columns, and make breaking
changes a deliberate, visible act.

### III. Documented Contract Conventions
An endpoint without a published OpenAPI schema is not done. Beyond the schema,
the contract is uniform:
- **Errors** use RFC 9457 `ProblemDetails` — a single, documented error shape.
- **Collections** use one consistent pagination envelope and strategy across
  every list endpoint (decided once, applied everywhere).
**Rationale:** "Documented" means the *whole* contract, not just the happy
path. Consumers integrate against error and paging conventions as much as
against success bodies.

### IV. Multi-Tenancy by Design
The tenancy model is: Organization -> Team -> User, connected by **Membership**
records. A User may belong to many Organizations, and within an Organization may
hold multiple Team memberships (these are membership edges, not strict
containment). The system uses a **shared schema with a tenant discriminator** —
not database-per-tenant. Every tenant-scoped table carries `OrganizationId`.
**Rationale:** Shared-schema multitenancy is the cost- and operability-correct
choice at demo scale; the discriminator column is the anchor the isolation
guarantees in Principle V attach to.

### V. Isolation at the Persistence Layer
Tenant isolation is enforced at the persistence layer via EF Core **global query
filters**, never by discipline in individual handlers. A repository method
capable of returning cross-tenant rows for a tenant-scoped entity is a defect.
**Identity/control-plane carve-out:** the identity graph — `User`,
`Organization`, and `Membership` — is intentionally NOT tenant-scoped and is not
subject to the ambient tenant filter, because resolving "which organizations
does this user belong to" is inherently cross-organization. Exactly one
sanctioned code path (the org-resolution service, see Principle VI) may use
`IgnoreQueryFilters`; use of `IgnoreQueryFilters` anywhere else is a defect.
**Rationale:** Isolation by convention fails silently. Centralizing it in query
filters makes the safe path the default path; naming the single legitimate
bypass keeps that default from being quietly eroded.

### VI. Server-Resolved, Verified Tenant Context
The organization context is resolved per request and MUST be verified against
the authenticated user's Membership records on every request. The ambient tenant
used by query filters is ALWAYS the server-resolved, membership-verified value —
never the raw client-supplied identifier. An unverified org identifier reaching
a query is a critical defect. The ambient value MUST be established before any
tenant-scoped query executes, and safely under DbContext/connection pooling.
**Rationale:** This is the crux of the whole system. Query filters are only as
trustworthy as the value they filter on; an attacker-supplied `OrganizationId`
that reaches the filter defeats every other guarantee.

### VII. Identity and Authorization Are Separate
Entra External ID handles **authentication only**. Organization membership,
teams, and roles live in the application database — never as IdP claims. Tokens
carry user identity; they do not carry authorization state.
**Rationale:** Authorization state changes constantly (invites, role changes,
removals) and must be revocable immediately. Baking it into tokens makes it
stale and unrevokable until expiry.

### VIII. Unified, Policy-Based Authorization
Roles are hierarchical and scoped: org-level (Owner, Admin, Member) and
team-level (Lead, Contributor). All authorization flows through **one policy
layer**. Ad-hoc role-string comparisons in endpoints are prohibited.
**Rationale:** A single policy layer is auditable and testable; scattered string
checks are where privilege-escalation bugs hide. ("Member" here is exclusively
the org role — the person entity is `User`.)

### IX. Contract-First, Isolation-Proven Testing
Every endpoint has integration tests written against the **public contract**,
not internal services. Two categories are mandatory, not afterthoughts:
- **Isolation:** every tenant-scoped entity has a test proving cross-tenant
  reads/writes are impossible.
- **Cross-tenant attack:** deliberate cross-tenant access attempts are part of
  the suite.
Test data is produced by a deterministic seeder so fixtures are reproducible.
**Rationale:** Testing against the public contract is the only way to prove the
guarantees a real client depends on. Isolation that isn't continuously proven is
isolation you don't actually have.

## Technology & Platform Constraints

- **Stack:** .NET / C# with .NET Aspire orchestration; React + TypeScript;
  Azure SQL; EF Core with migrations maintained from commit one.
- **AppHost is the single source of truth for topology.** Service composition,
  references, and the scale/serverless settings in the Deployment section are
  defined once in the Aspire AppHost — not duplicated in per-service config. The
  synthesized infrastructure (`azd infra synth` Bicep / `aspire-manifest.json`)
  is the artifact reviewed against the Deployment & Cost Constraints.
- **Deterministic data.** A seeder produces a known set of Organizations, Teams,
  Users, Memberships, and sample work items for demos and tests.

## Deployment & Cost Constraints

**Posture: always-on, idle-optimized.** The system stays deployed and reachable
at a stable URL, but idles to near-zero cost.

- Every Container App scales to zero (`minReplicas = 0`).
- Azure SQL uses the **free serverless offer with auto-pause** (`GP_S_Gen5_1`,
  `useFreeLimit: true`, `freeLimitExhaustionBehavior: 'AutoPause'`, short
  auto-pause delay). Cold resume latency is accepted.
- The React app is hosted on **Azure Static Web Apps (Free)**.
- The ACA environment uses the **Consumption** plan — never a dedicated
  workload profile.
- The **only permitted standing cost** is the container registry (ACR Basic,
  ~$5/mo; a public registry may be used instead for $0).

**No idle-billable dependencies.** The Aspire AppHost MUST NOT introduce any
resource that cannot scale to zero or that bills while idle — Azure Cache for
Redis, always-on Redis/RabbitMQ/Kafka containers, dedicated ACA workload
profiles, or persistent volumes. Where caching is needed, prefer in-memory. Any
such dependency MUST be flagged and justified at `/plan` time **before**
implementation.

**Pre-deploy verification.** Before deploying, the synthesized Bicep / Aspire
manifest is reviewed to confirm no `minReplicas > 0` on any service and no
idle-billable resource (e.g. `Microsoft.Cache/redis`,
`Microsoft.DBforPostgreSQL`, dedicated `workloadProfiles`) was added. A budget
alert at $1 is kept as a backstop.

## Governance

This constitution supersedes ad-hoc practice. It is the authority the SpecKit
**Constitution Check** gate evaluates every plan against.

- **Amendments** are made by a pull request that edits this document, states the
  rationale, and bumps the version.
- **Versioning (semantic):**
  - **MAJOR** — a principle is removed or redefined in a backward-incompatible
    way.
  - **MINOR** — a new principle or materially new section/guidance is added.
  - **PATCH** — clarification or wording that does not change obligations.
- **Compliance:** every `/plan` must pass the Constitution Check or record an
  explicit, justified exception. A merged change that violates a principle
  without such an exception is a defect to be reverted or amended, not
  grandfathered.

**Version:** 1.0.0 | **Ratified:** 2026-07-27 | **Last Amended:** 2026-07-27
