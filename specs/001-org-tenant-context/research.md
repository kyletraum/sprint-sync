# Phase 0 Research: Organization Onboarding & Tenant Context

The stack is fixed by the constitution, so research focuses on the *mechanisms*
that make the spec's guarantees structural rather than on choosing technologies.

## R1. Server-resolved, membership-verified tenant context (Principles VI, V)

**Decision**: A scoped `ITenantContext` holds the ambient `OrganizationId?` for
the request. It is populated by an ordered pipeline, *after* authentication:

1. **User resolution + JIT provisioning** — read the stable external id claim
   (Entra `oid`) from the validated token; look up `User` by `ExternalId`;
   create it if absent (0 memberships). Attach the app `User` to the request.
2. **Tenant resolution** — determine the *requested* org from (a) an explicit
   `X-Organization-Id` header / route value when present, else (b) the user's
   persisted `ActiveOrganizationId`. **Verify** a `Membership` links this user to
   that org. Only on success is `ITenantContext.OrganizationId` set to that
   (server-resolved, verified) value. A client-supplied id with no matching
   membership resolves to "no context" → org-scoped access returns 404.
   **Stale-active-org fallback (FR-014)**: if the persisted `ActiveOrganizationId`
   has no current membership (stale/invalid), it is never used as the ambient
   tenant; on resume the active org is re-resolved to another organization the
   user belongs to, or to the empty state (null) when they belong to none. A
   stale stored value never grants access.

**Rationale**: This puts Principle VI's "the ambient tenant is the server-
resolved value, never the raw client value" into a single choke point. Endpoints
never read an org id from the client directly.

**Alternatives considered**: Trusting an org id from the route/body in each
handler (rejected — that is the "unverified org identifier reaching a query"
critical defect); encoding org/role in the token (rejected — violates Principle
VII, and authz state must be revocable immediately).

## R2. EF Core global query filters under the right DbContext lifetime

**Decision**: Define a `TenantScopedEntity` base carrying `OrganizationId`. In
`OnModelCreating`, apply a global query filter
`e.OrganizationId == _tenant.OrganizationId` for every `TenantScopedEntity`,
where `_tenant` is the `ITenantContext` injected into the `AppDbContext`. Register
the context with **`AddDbContext` (scoped, NOT pooled)**.

**Rationale**: A global query filter captures a reference read at query time. With
a **scoped** context, `ITenantContext` is resolved per request, so the filter
sees the correct verified org. `AddDbContextPool` would capture services once at
pool-fill and reuse a stale tenant across requests — precisely the pooling hazard
the constitution's Principle VI warns about. At demo scale the throughput cost of
skipping pooling is irrelevant; correctness wins.

**Alternatives considered**: DbContext pooling with an interceptor that resets
tenant state per lease (rejected — more moving parts, easy to get subtly wrong);
manual `.Where(OrganizationId == ...)` in queries (rejected — violates Principle
V, "isolation by discipline in handlers is a defect").

**This slice**: no concrete `TenantScopedEntity` yet (see plan's interpretation
note); the base + filter wiring is established and unit-tested with a throwaway
scoped entity in tests to prove the filter denies cross-org rows, so the
mechanism is verified before the first real tenant entity arrives.

## R3. The identity/control-plane carve-out and the single IgnoreQueryFilters path

**Decision**: `User`, `Organization`, and `Membership` are **not**
`TenantScopedEntity` and carry **no** global filter. The one sanctioned place
that reads across orgs is the tenant-resolution/membership service (e.g.
"list the orgs this user belongs to", "does a membership exist"). If any of these
ever need `IgnoreQueryFilters`, it lives only there.

**Rationale**: "List my organizations" and "verify membership" are inherently
cross-organization reads; filtering them would break Principle IV ("users may
belong to many organizations"). The constitution's Principle V explicitly
carves this out; we localize it so it cannot erode.

**Alternatives considered**: Making `Membership` tenant-scoped and always using
`IgnoreQueryFilters` to read it (rejected — normalizes filter-bypassing and
defeats the point).

## R4. "Hide existence" access semantics — timing-oracle resistant (spec FR-009, Q2)

**Decision**: Any org-scoped access by a non-member returns **404 Not Found** with
a `ProblemDetails` body byte-identical to that of a genuinely nonexistent org.
Setting the active organization to a non-member org also returns 404. 403 is
never used for tenancy (403 confirms existence).

FR-009's timing clause is met **by construction**, not by tolerance:

1. **Single uniform query.** Both "org does not exist" and "org exists but caller
   is not a member" resolve through one membership-gated query
   (`… WHERE o.Id = @id AND EXISTS (SELECT 1 FROM Memberships m WHERE
   m.OrganizationId = o.Id AND m.UserId = @caller)`). The handler performs **no**
   separate "does this org exist?" lookup — so there is no existence-vs-membership
   branch and no extra round-trip to tell the cases apart: identical query,
   identical empty result, identical 404 + ProblemDetails.
2. **Authorization denials map to 404, not 403.** Where the `OrgMember` policy
   (R6) guards a hide-existence resource, a custom authorization result handler
   converts denial into the same 404/ProblemDetails. Authorization stays in the
   one policy layer (Principle VIII) while never emitting a 403 that would confirm
   existence.
3. **Regression guard.** A timing-parity test (T034) asserts the non-member and
   unknown-id cases stay within a tolerance band, catching any future change that
   reintroduces a divergent path or round-trip.

This closes the *algorithmic* timing oracle — the one an attacker can actually
exploit at the API boundary. Microsecond-level constant-time against cache/JIT/GC
jitter is not an app-layer property and is not the threat FR-009 targets; the
uniform-path design removes every structural signal an attacker could use.

**Rationale**: Prevents membership/existence enumeration by probing ids (FR-009,
SC-002); uniform ProblemDetails satisfies Principle III; the 404 mapping preserves
Principle VIII.

## R5. Authentication (Entra External ID) and JIT provisioning (Principle VII)

**Decision**: `Microsoft.Identity.Web` validates JWT bearer tokens from the Entra
External ID (CIAM) tenant. The stable `oid` claim is the `User.ExternalId`.
Account creation ("sign up") happens entirely in Entra's hosted user flow; the
API provisions its local `User` just-in-time on first authenticated request
(spec FR-013). The React app uses MSAL to sign in and attach the bearer token.

**Rationale**: Straightforward CIAM integration; keeps identity in the IdP and
authorization state in the DB.

**Alternatives considered**: Building local auth (rejected — constitution
mandates Entra); syncing memberships as token claims (rejected — Principle VII).

## R6. Authorization policy layer (Principle VIII)

**Decision**: One `OrgMember` authorization requirement + handler that checks the
resolved `ITenantContext`/membership. Org-scoped endpoints require the
`OrgMember` policy; org creation and `me`/list endpoints require only an
authenticated user. Roles are modeled (`OrgRole.Owner`) for future policies but
this slice needs no Owner-only endpoint. No role strings compared inline. For
hide-existence resources, policy denials are mapped to **404** (not 403) via a
custom authorization result handler, so authorization never confirms existence
(R4).

**Rationale**: Centralized, auditable authorization; extends cleanly when
Admin/Member and team roles arrive.

## R7. API surface, versioning, OpenAPI, pagination (Principles II, III)

**Decision**:
- Minimal APIs grouped by feature, under `/api/v1` via `Asp.Versioning.Http`.
- OpenAPI generated by `Microsoft.AspNetCore.OpenApi`, served at
  `/openapi/v1.json` (the published deliverable).
- `AddProblemDetails()` + a global exception handler produce RFC 9457 errors.
- **One pagination convention**: offset-based, envelope
  `{ items, page, pageSize, totalCount }`, applied to all list endpoints
  (`GET /organizations`). Generous default page size; org lists are small now but
  the convention is fixed here so later list endpoints inherit it.

**Rationale**: Satisfies "documented as a deliverable" and "one consistent
pagination strategy."

**Alternatives considered**: Cursor pagination (rejected for now — offset is
simpler and adequate at demo scale; revisit if a high-volume list appears).

## R8. Aspire topology & cost controls (Principles X, 12)

**Decision**: The AppHost declares exactly three resources: the Azure SQL
database, the API project (referencing SQL), and the React app (npm app, local
dev). No Redis/RabbitMQ/broker. At publish:
- API → Azure Container App with `minReplicas = 0`.
- SQL → free serverless offer (`GP_S_Gen5_1`, `useFreeLimit: true`,
  `freeLimitExhaustionBehavior: 'AutoPause'`, short auto-pause delay), configured
  via `ConfigureInfrastructure`.
- React → **Azure Static Web Apps Free** (deployed via `azd`, outside ACA to
  preserve the ACA free grant). The AppHost remains the source of truth for the
  API+SQL topology and references the web app for local dev.
- Pre-deploy: `azd infra synth` reviewed for `minReplicas: 0` and absence of
  idle-billable resources.

**Rationale**: Meets the always-on/idle-optimized posture at near-zero cost.

**Alternatives considered**: Serving React from the API/ACA (rejected — burns the
ACA compute grant and couples the SPA to the API lifecycle); Azure Cache for
Redis for the active-org/session (rejected — always-on billable; active org is
persisted in SQL instead).

## R9. Testing strategy (Principle IX)

**Decision**: xUnit integration tests via `WebApplicationFactory` exercise the
public HTTP contract. A **test authentication handler** injects a configurable
external id so tests simulate distinct users without Entra. SQL runs in a
Testcontainers SQL Server (or the Aspire test host) with real migrations applied.
First-class test classes:
- Onboarding (create → Owner → appears in list; JIT provisioning; empty state).
- Membership listing (only my orgs; excludes others).
- **Cross-tenant attack** (non-member GET org → 404 indistinguishable; set-active
  to non-member → 404; stale membership → access stops).
- Active-org switch takes effect on next request.
- Query-filter mechanism proven with a throwaway scoped entity (R2).

**Rationale**: Tests target the contract a real client depends on and make
cross-tenant attempts a permanent part of the suite, per Principle IX.

**Alternatives considered**: In-memory EF provider (rejected — does not honor
relational query filters / SQL semantics faithfully; isolation tests must run on
real SQL).
