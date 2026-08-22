<!--
SYNC IMPACT REPORT
==================
Version change: 1.0.0 -> 1.1.0  (MINOR: one new principle + materially tightened
deployment guidance)

Rationale: The feature 001 review committee surfaced two obligations that were
real but unwritten, and so had no gate to fail against:
  1. Health probes and telemetry export were treated as optional P2 polish. A
     deployed service you cannot probe or trace is not operable, and nothing in
     v1.0.0 said so.
  2. "Pre-deploy verification" reviewed the resources present under `infra/`
     without asking whether they are actually deployed. An orphaned
     `containerApps` module unreachable from `main.bicep` satisfied the cost
     guard while contributing nothing to the running system — presence stood in
     for deployment.

Added:
  - Principle X (Operable by Default) — liveness/readiness endpoints exposed and
    wired as platform probes; OpenTelemetry export enabled when the environment
    supplies a destination; failure to observe is a defect, not a nice-to-have.

Modified:
  - Deployment & Cost Constraints -> "Pre-deploy verification": the reviewed set
    is now the resources applied by a *declared deploy path* (provision via
    `main.bicep`, azd's per-service deploy module, or an `azure.yaml` hook), not
    every `.bicep` file under `infra/`. Infrastructure on no path is a defect.
    NOTE: an earlier draft of this amendment made bare reachability from
    `main.bicep` the test. Verifying it against the repo disproved that — both
    `infra/budget.bicep` (hook-deployed) and `infra/api/api-containerapp.module.bicep`
    (azd deploy-time module) are unreachable from `main.bicep` yet correctly
    deployed. The three-path form is what survived contact with the code.

Templates / follow-ups:
  - `.specify/templates/plan-template.md` references the Constitution Check
    generically (no enumerated principle list) — no template edit required.
  - `specs/001-org-tenant-context/tasks.md`: T057 becomes implementation of
    Principle X, and T056 becomes enforcement of the tightened verification
    rule, rather than free-floating P2 items.

--- prior report: (none) -> 1.0.0 (initial ratification) ---
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

### X. Operable by Default
A service that is deployed but cannot be probed or traced is not done.
- Every service exposes a **liveness** and a **readiness** endpoint, and the
  deployed platform is configured to actually call them — an exposed endpoint no
  orchestrator probes proves nothing.
- **OpenTelemetry export is enabled whenever the environment supplies a
  destination** (e.g. `APPLICATIONINSIGHTS_CONNECTION_STRING`). Instrumentation
  that is collected but never exported is not observability.
- Probe wiring and the export destination are defined in the Aspire AppHost,
  consistent with the AppHost-as-topology rule below, and MUST NOT introduce a
  standing cost that violates the Deployment & Cost Constraints.
**Rationale:** The first real incident is the wrong time to discover there is no
signal. Probes are also what make `minReplicas = 0` safe — scale-from-zero and
rolling revisions depend on the platform knowing when an instance is genuinely
ready to take traffic.

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

The reviewed set is the resources that some **declared deploy path** actually
applies — not every `.bicep` file present under `infra/`. There are three such
paths, and a template is legitimate if it sits on any one of them:

1. **Provision** — reachable from `infra/main.bicep` by module reference.
2. **Service deploy** — the per-service module azd applies at `azd deploy`
   (recognisable by its image parameter, which cannot exist at provision time;
   its other parameters are fed by `main.bicep` outputs).
3. **Declared hook** — a template invoked by a hook in `azure.yaml`.

A `.bicep` file on **none** of these paths is dead infrastructure and MUST be
wired in or deleted. Verification checks MUST NOT count a template toward a
posture claim unless it sits on a deploy path, because presence under `infra/`
would otherwise let the guard report a posture the running system does not have.
Reachability from `main.bicep` alone is **not** the test — applying it as such
falsely condemns paths 2 and 3.

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

**Version:** 1.1.0 | **Ratified:** 2026-07-27 | **Last Amended:** 2026-08-22
