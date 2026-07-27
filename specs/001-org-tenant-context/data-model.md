# Phase 1 Data Model: Organization Onboarding & Tenant Context

Shared-schema multi-tenancy (Principle IV). Three entities in this slice, all
**control-plane** (outside the global query filter — the Principle V carve-out).
A `TenantScopedEntity` base is defined for future work-data tables but has no
concrete subclass yet.

## Entity: User

The application's local mirror of an externally-authenticated identity.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `Guid` | PK, app-generated | Internal identity used by FKs. |
| `ExternalId` | `string(200)` | Required, **unique index** | Entra External ID `oid` claim. |
| `DisplayName` | `string(200)?` | Nullable | Cached from token for convenience; not authoritative. |
| `ActiveOrganizationId` | `Guid?` | FK → `Organization.Id`, nullable, `ON DELETE SET NULL` | Persisted active-org selection (FR-014). **Re-verified against Membership on every use — never grants access on its own.** |
| `CreatedAt` | `DateTimeOffset` | Required | Set on JIT provisioning. |

- **Provisioning**: created just-in-time on first authenticated request (FR-013).
- **Tenant scope**: none — a user spans organizations.

## Entity: Organization

A tenant boundary and the root all org-scoped data will hang from.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `Guid` | PK, app-generated | This is the tenant discriminator value other tables will carry as `OrganizationId`. |
| `Name` | `string(100)` | Required, non-empty/non-whitespace, max 100 (FR-012) | Not globally unique (spec assumption). |
| `CreatedByUserId` | `Guid` | FK → `User.Id`, required | The founding Owner. |
| `CreatedAt` | `DateTimeOffset` | Required | |

- **Tenant scope**: the tenant itself — not globally filtered. Access to a
  specific organization is gated by Membership (→ 404 for non-members).

## Entity: Membership

The sole authority for what a user may see or do within an organization.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `Guid` | PK | |
| `UserId` | `Guid` | FK → `User.Id`, required, `ON DELETE CASCADE` | |
| `OrganizationId` | `Guid` | FK → `Organization.Id`, required, `ON DELETE CASCADE` | Carried here for the join, but Membership is **not** query-filtered. |
| `Role` | `OrgRole` (enum) | Required | `Owner` only in this slice; `Admin`, `Member` reserved (Principle VIII). |
| `CreatedAt` | `DateTimeOffset` | Required | |

- **Unique constraint**: `(UserId, OrganizationId)` — a user has at most one
  membership per org.
- **Tenant scope**: identity/control-plane carve-out — **not globally filtered**,
  because "list my orgs" and membership verification are cross-org reads
  (Principle V carve-out, research R3).

## Base (future): TenantScopedEntity

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `OrganizationId` | `Guid` | Required, indexed | The discriminator every tenant-scoped table carries (Principle IV). |

- A global query filter `OrganizationId == ITenantContext.OrganizationId` is
  applied to all subclasses in `OnModelCreating` (research R2). **No concrete
  subclass exists in this slice**; it is the seam the first work-data entity uses.

## Relationships

```text
User 1───* Membership *───1 Organization
User *──────────(founder)──────────1 Organization   (Organization.CreatedByUserId)
User 0..1 ──(active selection)──> 1 Organization     (User.ActiveOrganizationId, nullable)
```

- A `User` has many `Membership` rows (one per org they belong to).
- An `Organization` has many `Membership` rows (its members; exactly one Owner in
  this slice — the creator).
- `User.ActiveOrganizationId` is a soft pointer; validity is decided by whether a
  corresponding `Membership` currently exists, not by the FK alone.

## Lifecycle / state

- **Create organization** (FR-001/002/003): in one transaction — insert
  `Organization` (with `CreatedByUserId = current user`), insert `Membership`
  `(user, org, Owner)`. If the user had no active org, set
  `User.ActiveOrganizationId = new org`.
- **Switch active org** (FR-006/007): verify a `Membership` exists for
  `(user, targetOrg)`; if yes set `ActiveOrganizationId`; if no return 404
  (hide existence, R4).
- **Resolve context per request** (FR-010/014): see research R1 — verify
  membership for the requested/persisted org; a stale/invalid `ActiveOrganizationId`
  falls back to another membership or the empty state, and never grants access.
- **Membership revoked mid-session** (edge case): removal is out of scope, but if
  a `Membership` disappears, the next request's verification fails and access
  stops immediately — no session-cached grant.

## Validation rules (from requirements)

- `Organization.Name`: trimmed, non-empty, ≤ 100 chars (FR-012) → 400
  `ProblemDetails` on violation.
- Active-org target must correspond to an existing `Membership` for the caller
  (FR-007) → else 404.
- `User.ExternalId` unique (one local user per external identity, FR-013).

## Indexing

- `User.ExternalId` — unique (hot path: resolve user on every request).
- `Membership (UserId, OrganizationId)` — unique; also serves membership
  verification and "list my orgs".
- `Membership.OrganizationId` — for future "members of org" reads.
