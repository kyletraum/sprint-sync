# Feature Specification: Organization Onboarding & Tenant Context

**Feature Branch**: `001-org-tenant-context`

**Created**: 2026-07-27

**Status**: Draft

**Input**: User description: "Organization onboarding and tenant context. A signed-in user can create and work within organizations in a multi-tenant work tracker. On creation they become Owner; they may belong to many organizations; they choose an active organization and can switch it; while active in one organization they only ever see that organization's data; they can only act within organizations they are a member of. Out of scope: teams, roles beyond Owner, inviting users, and work items."

## Clarifications

### Session 2026-07-27

- Q: How does a user first become known to the application (provisioning model)? → A: Just-in-time — the app creates a User record automatically on the first authenticated request from a not-yet-known identity; account creation ("sign up") is handled entirely by the external identity provider, and there is no separate in-app registration step.
- Q: When a user targets an organization they do not belong to, hide its existence or acknowledge-but-forbid? → A: Hide existence — respond with a "not found" outcome that is indistinguishable from a genuinely nonexistent organization, so membership cannot be enumerated by probing identifiers.
- Q: How is the active organization remembered between sessions? → A: Persisted per user server-side — the last active organization is stored and resumed on return after membership re-verification; a stale/invalid selection falls back to another valid organization or the empty-state prompt.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create an organization and become its Owner (Priority: P1)

A signed-in user creates a new organization by giving it a name. The moment it
is created, they are recorded as the organization's Owner, and the new
organization appears in their list of organizations.

**Why this priority**: This is the entry point to the entire product. Without
the ability to create an organization, a new user has nowhere to work and no
later feature is reachable. It is the smallest slice that delivers standalone
value.

**Independent Test**: Sign in as a brand-new user, create an organization named
"Acme", and confirm that Acme now appears in the user's organization list with
the user shown as its Owner.

**Acceptance Scenarios**:

1. **Given** a signed-in user with no organizations, **When** they create an
   organization named "Acme", **Then** the organization is created, the user is
   its Owner, and it appears in their organization list.
2. **Given** a signed-in user, **When** they attempt to create an organization
   with an empty name, **Then** creation is rejected with a clear reason and no
   organization is created.
3. **Given** a signed-in user who already owns "Acme", **When** they create a
   second organization named "Beta", **Then** both organizations exist and the
   user is Owner of each.

---

### User Story 2 - See the organizations I belong to (Priority: P1)

A user can retrieve the list of every organization they are a member of, and
only those organizations. This list is available regardless of which
organization is currently active.

**Why this priority**: A user must be able to find and choose among their
organizations before they can do anything within one. It is also the first
observable proof that membership — not guesswork — governs what a user can
reach.

**Independent Test**: Seed a user as a member of exactly two organizations and a
third organization the user does not belong to; confirm the user's list returns
exactly the two, never the third.

**Acceptance Scenarios**:

1. **Given** a user who belongs to organizations "Acme" and "Beta", **When**
   they request their organizations, **Then** the result contains exactly "Acme"
   and "Beta".
2. **Given** an organization "Gamma" the user is not a member of, **When** the
   user requests their organizations, **Then** "Gamma" does not appear.
3. **Given** a newly signed-in user who belongs to no organizations, **When**
   they request their organizations, **Then** an empty list is returned (not an
   error) and they are prompted to create one.

---

### User Story 3 - Choose and switch the active organization (Priority: P2)

A user selects which organization they are currently working in — their active
organization — and can change it at any time. The active organization determines
the context for everything they subsequently see and do.

**Why this priority**: Belonging to many organizations is only useful if the
user can move between them. It builds directly on Stories 1 and 2 but is not
required to prove those slices, so it follows them.

**Independent Test**: As a user who belongs to "Acme" and "Beta", set the active
organization to "Acme", then switch it to "Beta", and confirm each switch takes
effect for the next action.

**Acceptance Scenarios**:

1. **Given** a user who belongs to "Acme" and "Beta", **When** they set their
   active organization to "Beta", **Then** their subsequent actions operate in
   the context of "Beta".
2. **Given** a user with an active organization of "Acme", **When** they switch
   to "Beta", **Then** the change takes effect immediately for the next action.
3. **Given** a user, **When** they attempt to set their active organization to
   one they are not a member of, **Then** the request is rejected and the active
   organization is unchanged.

---

### User Story 4 - Never see or act on an organization I do not belong to (Priority: P1)

While working in an active organization, a user only ever sees data belonging to
that organization. A user can never see or act within an organization they are
not a member of — even a user who belongs to several organizations sees each
organization's data only within that organization's context.

**Why this priority**: This is the safety-critical guarantee the whole
multi-tenant product rests on. A single leak here is a serious defect, so it is
proven from the first slice rather than added later.

**Independent Test**: With a user who belongs to "Acme" but not "Gamma", attempt
to view or act within "Gamma" by every available path and confirm each attempt
is rejected; confirm no "Gamma" data is ever returned.

**Acceptance Scenarios**:

1. **Given** a user who belongs to "Acme" but not "Gamma", **When** they attempt
   to view or operate within "Gamma", **Then** the response is a "not found"
   outcome indistinguishable from that of a nonexistent organization, and no
   "Gamma" data is disclosed.
2. **Given** a user active in "Acme", **When** they view organization data,
   **Then** they see only "Acme" data and nothing belonging to any other
   organization.
3. **Given** a user who belongs to both "Acme" and "Beta" and is active in
   "Acme", **When** they view organization data, **Then** they see "Acme" data
   only and no "Beta" data leaks into the view.
4. **Given** a request that supplies an organization identifier the user is not a
   member of, **When** the request is processed, **Then** the acting
   organization is taken from the user's verified memberships and the request is
   rejected — the supplied identifier alone never grants access.

---

### Edge Cases

- **No memberships**: a signed-in user who belongs to no organization sees an
  empty list and a prompt to create one — never an error or a blank failure.
- **Membership revoked mid-session**: if a user's membership in the active
  organization ends, their next action in that organization is rejected; access
  stops immediately rather than persisting for the session. (The act of removing
  members is itself out of scope for this slice.)
- **Stale active organization**: if a user's active organization is one they no
  longer belong to (or never did), the system does not grant access on the
  strength of that stored selection.
- **Duplicate organization names**: two organizations may share the same name;
  a name collision does not merge, block, or confuse them (each is distinct).
- **Empty or whitespace-only name**: rejected at creation.
- **Excessively long name**: rejected or truncated per the documented name
  limit.
- **Many organizations**: a user who belongs to a large number of organizations
  can still retrieve their full list.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow a signed-in user to create a new organization by
  providing a non-empty name.
- **FR-002**: On creation, the system MUST record the creating user as the new
  organization's Owner.
- **FR-003**: A newly created organization MUST contain only its creating Owner
  and no other members or data.
- **FR-004**: System MUST allow a single user to belong to multiple organizations
  at the same time.
- **FR-005**: System MUST let a user retrieve the list of all organizations they
  are a member of, and MUST exclude every organization they are not a member of.
- **FR-006**: System MUST allow a user to select an active organization and to
  change it at any time.
- **FR-007**: System MUST reject an attempt to set the active organization to one
  the user is not a member of, leaving the previous selection unchanged.
- **FR-008**: System MUST scope all organization-specific data a user sees to
  their active organization only, with no data from any other organization
  included.
- **FR-009**: System MUST reject any attempt by a user to read or act within an
  organization they are not a member of by responding with a "not found"
  outcome that is indistinguishable from that of a genuinely nonexistent
  organization. The response MUST NOT reveal — by status, message, or timing
  difference — whether the organization exists, so that membership cannot be
  enumerated by probing identifiers.
- **FR-010**: System MUST determine the organization a request operates in from
  the server's verified record of the user's memberships on every request; an
  organization identifier supplied by the client MUST NOT, on its own, grant
  access or set the acting context.
- **FR-011**: System MUST make a user's membership across all their organizations
  retrievable to that user (the cross-organization list of FR-005), while all
  organization-scoped data remains confined to a single organization at a time.
- **FR-012**: System MUST reject organization creation when the name is empty or
  whitespace-only, and MUST enforce a documented maximum name length.
- **FR-013**: System MUST create a User record automatically on the first
  authenticated request from an identity it has not seen before (just-in-time
  provisioning), with no separate in-app registration step; account creation is
  performed by the external identity provider.
- **FR-014**: System MUST persist each user's active-organization selection
  server-side and resume it on the user's return. On resume, membership MUST be
  re-verified; if the persisted selection is stale or invalid, the system MUST
  fall back to another organization the user belongs to, or to the empty-state
  "create your first organization" prompt when they belong to none. A persisted
  selection MUST NOT, on its own, grant access (see FR-010).

### Key Entities *(include if feature involves data)*

- **User**: An authenticated person. Identified by a stable identity established
  at sign-in. The application's User record is created just-in-time on first
  authenticated request (see FR-013). A user may belong to many organizations.
- **Organization**: A tenant boundary. Has a name and an Owner, and contains its
  members and (in later features) its work data. All organization-scoped data
  belongs to exactly one Organization.
- **Membership**: The association between a User and an Organization, carrying
  the user's role within it (Owner is the only role in this slice). Membership is
  the sole authority for what a user may see or do in an organization.
- **Active Organization Context**: The single organization a user is currently
  working within. Persisted per user server-side (see FR-014) and re-verified
  against membership on use. Governs the scope of everything the user sees and
  does until changed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can create an organization and see themselves
  listed as its Owner within a single, uninterrupted flow (no more than one
  screen/step beyond naming it).
- **SC-002**: 100% of attempts to view or act within an organization the user is
  not a member of are rejected with a "not found" outcome indistinguishable from
  a nonexistent organization — zero cross-organization data is ever disclosed and
  no signal reveals that the organization exists.
- **SC-003**: A user who belongs to exactly N organizations sees exactly those N
  in their list — no more and no fewer — across repeated checks.
- **SC-004**: A user who belongs to two organizations sees zero data from the
  non-active organization while working in the active one, verified by direct
  attempt.
- **SC-005**: Switching the active organization takes effect on the very next
  action, with no residual data or access from the previous organization.
- **SC-006**: A new user with no organizations reaches a "create your first
  organization" path 100% of the time, and never an error state.

## Assumptions

- **Authentication is provided externally.** A signed-in user's identity is
  already available to the system; building sign-in, registration, or account
  management is out of scope for this feature. Account creation ("sign up") is
  performed by the external identity provider; the application only mirrors the
  identity into a local User record just-in-time on first authenticated request.
- **Organization names need not be globally unique.** Two organizations may share
  a name; names are for human recognition, not identity. Maximum name length is a
  documented limit (assumed ~100 characters) and empty/whitespace names are
  rejected.
- **Active organization is remembered per user.** A user's active-organization
  selection persists across sessions until they change it; a user's first (or
  only) organization may be treated as active by default.
- **Membership governs everything.** A user's ability to see or act within an
  organization is decided solely by whether a current membership record links
  them to it — never by a client-supplied value or a stored selection alone.
- **Out of scope (later features):** teams, roles beyond Owner, inviting or
  removing members, deleting organizations, and work items themselves.
