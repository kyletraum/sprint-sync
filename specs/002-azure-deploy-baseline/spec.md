# Feature Specification: Azure Deployment & Operability Baseline

**Feature Branch**: `002-azure-deploy-baseline`

**Created**: 2026-08-22

**Status**: Draft

**Input**: User description: "Azure Deployment & Operability Baseline — take Sprint Sync from 'builds and passes tests locally' to 'provably running in Azure, observable, and cost-guarded.'"

## Context

Sprint Sync currently builds, tests, and passes every guard **locally**. Feature
001 (Organization Onboarding & Tenant Context) is code-complete with 56/56
backend integration tests and 5/5 frontend tests green, but it has never run in
Azure. Its isolation guarantees — the entire point of a multi-tenant tracker —
are proven only against Testcontainers, never against a deployed system with a
real identity provider in front of it.

This feature closes that gap. It is the difference between "we believe this is
multi-tenant" and "we have watched a second tenant fail to read the first
tenant's data over the public internet."

### Relationship to Feature 001

Feature 001's **Principle IX evidence gate is delegated to this feature**.
Feature 001 MUST NOT be claimed production-done until this feature's live
validation (User Story 5) passes. Tasks T055–T058 previously tracked in
`specs/001-org-tenant-context/tasks.md` (Phase 9B) are absorbed here and removed
there; this spec is now their single home.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator can sign in to a deployed Sprint Sync (Priority: P1)

An operator with no prior Azure setup follows a documented sequence and ends up
with a running Sprint Sync in Azure — API **and** web app — that a real person
can reach in a browser and sign in to with a real identity. Today this is
impossible twice over: no identity tenant exists and the preprovision guard
correctly refuses to deploy without one, and nothing deploys the web
application at all.

**Why this priority**: Nothing else in this feature can be verified until the
system is deployed and reachable. Every other story depends on this one having
happened. It is also the story that converts the largest unknown — "does any of
this actually work outside a test harness?" — into a fact.

**Independent Test**: Run the documented setup from a clean environment; confirm
a browser can reach the deployed web app, sign in with a real identity, and see
the signed-in user's own organization context. Delivers a working, reachable
product.

**Acceptance Scenarios**:

1. **Given** no identity tenant exists, **When** the operator follows the
   documented identity setup, **Then** they obtain all four configuration values
   the deployment requires, and the configuration guard passes.
2. **Given** the four identity values are configured, **When** the operator runs
   the deployment, **Then** the API service provisions successfully and starts
   without crash-looping.
3. **Given** the API is deployed, **When** its runtime configuration is
   inspected, **Then** all four identity settings are present and **non-empty**
   — not blank, not placeholder text.
4. **Given** the deployment succeeded, **When** the web application is deployed,
   **Then** it is reachable at a stable public URL and serves the built SPA.
5. **Given** the hosted web application, **When** a browser loads it and calls
   the API, **Then** the cross-origin request is accepted rather than blocked.
6. **Given** the deployment succeeded, **When** a real person signs in through
   the **hosted** web app, **Then** they are authenticated and see their own
   organization, having been provisioned on first sign-in.
7. **Given** an operator with no prior context, **When** they follow the setup
   documentation start to finish, **Then** they reach a working deployment
   without needing to consult the implementers.

---

### User Story 2 - Isolation is proven against the deployed system (Priority: P1)

A reviewer can demonstrate that cross-tenant access fails on the **real,
deployed** API — not merely in a local test harness. Two tenants exist, and one
provably cannot see or affect the other's data over the public endpoint.

**Why this priority**: This is the product's core promise and the constitution's
Principle IX obligation. Feature 001 asserts isolation but has never
demonstrated it outside Testcontainers. An isolation guarantee that has only
ever been tested locally is a claim, not evidence. Equal priority to Story 1
because Story 1 without Story 2 is a deployed system nobody should trust.

**Independent Test**: Point the existing cross-tenant attack suite at the
deployed API's public URL and confirm every attack is refused, with
non-existence indistinguishable from non-membership.

**Acceptance Scenarios**:

1. **Given** two organizations exist in the deployed system, **When** a member
   of one requests the other's resources, **Then** the request is refused.
2. **Given** a member of one organization, **When** they request a resource that
   does not exist at all, **Then** the response is **indistinguishable** from
   requesting a resource that exists but belongs to another organization.
3. **Given** a caller asserts membership of an organization they do not belong
   to, **When** the request is processed, **Then** the assertion is ignored and
   the caller's genuine context is used instead.
4. **Given** the attack suite, **When** it is run against the deployed API,
   **Then** every case passes with results recorded as durable evidence.

---

### User Story 3 - Operators can tell whether the system is healthy (Priority: P2)

When the deployed system misbehaves, an operator can find out **from signal**
rather than by guessing. The platform knows when an instance is genuinely ready
to take traffic, and request traces reach a place a human can query.

**Why this priority**: Below deployment and isolation because the system can be
correct without it — but only briefly. The first real incident is the wrong time
to discover there is no signal. Health probes are also what make scale-to-zero
safe: without a readiness signal, the platform routes traffic to instances that
are still starting.

**Independent Test**: Deploy, confirm the platform reports health based on the
application's own health endpoints, then exercise the API and confirm the
resulting traces are queryable.

**Acceptance Scenarios**:

1. **Given** the deployed service, **When** the platform evaluates instance
   health, **Then** it does so by calling the application's own liveness and
   readiness endpoints — not by assuming a started process is healthy.
2. **Given** an instance that has started but is not yet ready, **When** the
   platform evaluates readiness, **Then** traffic is withheld until it reports
   ready.
3. **Given** a telemetry destination is configured, **When** requests are served,
   **Then** traces for those requests are queryable within minutes.
4. **Given** **no** telemetry destination is configured, **When** the service
   starts, **Then** it starts normally and does not fail on the missing
   destination.
5. **Given** health probes are configured, **When** the deployment is applied,
   **Then** the service does not crash-loop — probe configuration is confirmed
   against a real deployment before being considered done.

---

### User Story 4 - The cost guard cannot be satisfied by undeployed infrastructure (Priority: P2)

The pre-deployment cost guard reports on what is **actually deployed**. An
infrastructure template that no deployment path applies cannot satisfy a guard
check, and infrastructure belonging to no deployment path is reported as a
defect.

**Why this priority**: The guard's job is preventing surprise spend, and it is
currently satisfiable by files that contribute nothing to the running system.
This is a correctness bug in a safety mechanism — the failure is silent, and it
fails in the direction of false confidence. Below Stories 1–3 because it
protects against a future regression rather than an active fault.

**Independent Test**: Introduce a template that no deployment path applies and
confirm the guard fails; confirm the guard still passes on the real
infrastructure, whose legitimately-deployed templates are not all reachable the
same way.

**Acceptance Scenarios**:

1. **Given** infrastructure containing a compute resource that no deployment
   path applies, **When** the guard runs, **Then** it fails and names the
   offending template.
2. **Given** infrastructure whose only zero-idle-cost declaration lives in an
   undeployed template, **When** the guard runs, **Then** it does **not** count
   that declaration as satisfying the zero-idle-cost requirement.
3. **Given** the current, correct infrastructure — which includes templates
   deployed by paths other than the main provisioning template — **When** the
   guard runs, **Then** it passes, with **no** false positives against those
   templates.
4. **Given** both the POSIX and Windows guard implementations, **When** each is
   run against the same infrastructure, **Then** they reach identical verdicts.

---

### User Story 5 - Continuous integration runs for real (Priority: P3)

The CI pipeline executes on a real runner and its result is trustworthy evidence
rather than an untested configuration file.

**Why this priority**: Lowest because it protects future changes rather than
establishing present correctness — but it is what keeps Stories 1–4 true after
the next commit.

**Status — largely already satisfied**: the pipeline has already run live
(workflow run 32537061874 on PR #3), with all four jobs — backend, web,
idle-cost guard, infra drift — passing, and job logs read to confirm they do
real work rather than passing vacuously. What remains is scenario 2 below
(confirming CI actually fails on a broken test) and re-confirming the run once
this feature's changes land. **Plan accordingly: this is a small story, not a
fresh build.**

**Independent Test**: Trigger the pipeline on a real runner and confirm it
completes, running the full test suite and reporting an accurate result.

**Acceptance Scenarios**:

1. ~~**Given** the CI configuration, **When** it runs on a real runner, **Then**
   it completes and runs the full backend and frontend test suites.~~
   **— already satisfied (run 32537061874).**
2. **Given** a change that breaks a test, **When** CI runs, **Then** it fails.
   This has **not** been demonstrated; a passing pipeline that cannot fail is
   not evidence.
3. **Given** this feature's infrastructure and operability changes, **When** CI
   runs after they land, **Then** it still passes — including the cost guard job,
   whose behaviour User Story 4 changes.

### Edge Cases

- **Identity values present but wrong** (valid-looking but not matching a real
  tenant): the configuration guard checks presence and placeholder text, not
  authenticity. A deployment can therefore pass the guard and still fail at
  sign-in. This MUST surface as a clear sign-in failure, not a silent
  half-working state — and the setup documentation must make the failure mode
  recognizable.
- **First request after idle**: with scale-to-zero and database auto-pause, the
  first request after an idle period pays a cold-start and database-resume cost.
  This is accepted by the constitution, but readiness must not report ready
  before the service can actually serve.
- **Telemetry destination configured but unreachable**: the service must
  continue serving. Telemetry export failure MUST NOT become an availability
  failure.
- **Deployment partially fails**: the operator must be able to determine what
  provisioned and what did not, and re-run without manual cleanup.
- **Budget alert recipients unset**: the cost backstop is currently skipped
  rather than failed. The operator must be told this happened rather than
  discovering the missing backstop on a surprise bill.
- **Two deployments racing**: concurrent deployments must not corrupt the
  database schema (feature 001 added a lock for this; this feature must not
  regress it).

## Requirements *(mandatory)*

### Functional Requirements

#### Identity infrastructure

- **FR-001**: The project MUST have a documented, repeatable procedure for
  standing up an external-identity tenant and registering the API and web
  application within it.
- **FR-002**: The procedure MUST produce all four configuration values the
  existing configuration guard requires, and MUST state where each value is
  found.
- **FR-003**: Sign-in MUST work for a real person through the web application,
  end to end, against the deployed API.
- **FR-004**: The procedure MUST NOT require secrets to be committed to the
  repository.

#### Deployment

- **FR-005**: A documented deployment command MUST provision the full topology
  and deploy the API from a clean environment.
- **FR-006**: The deployed API MUST receive all four identity settings as
  **non-empty** runtime configuration; a deployment that leaves any of them
  empty MUST be treated as failed.
- **FR-007**: Deployment MUST be re-runnable without manual cleanup between
  attempts.
- **FR-008**: The deployment MUST be verifiable — an operator MUST be able to
  confirm the API is reachable and correctly configured without reading
  application logs line by line.

#### Web application hosting

- **FR-027**: The web application MUST be deployed to a hosted, publicly
  reachable URL as part of the documented deployment procedure — not run
  locally against the deployed API.
- **FR-028**: Hosting MUST remain on the free tier, adding no standing cost, per
  the constitution's requirement that the React app is hosted on Static Web Apps
  (Free).
- **FR-029**: The SPA's build-time configuration (identity authority, client id,
  API scope, API base URL) MUST be supplied at build time from the deployment
  environment, never committed and never hardcoded to a developer's values.
- **FR-030**: The API MUST accept browser requests from the hosted web
  application's origin. Without this the hosted SPA cannot call the API at all.
  The permitted origin MUST be configured, not wildcarded.
- **FR-031**: The identity registration MUST list the hosted web application's
  redirect URI, so sign-in completes from the hosted origin.
- **FR-032**: Deploying the web application MUST be repeatable and automated,
  not a one-off manual upload.

#### Cost governance

- **FR-009**: The cost guard MUST classify each infrastructure template by
  whether some declared deployment path applies it, and MUST count only applied
  templates toward its posture checks.
- **FR-010**: The cost guard MUST fail when an infrastructure template belongs
  to no deployment path.
- **FR-011**: The cost guard MUST NOT report a false positive against templates
  that are legitimately deployed by a path other than the main provisioning
  template.
- **FR-012**: Both guard implementations MUST reach identical verdicts on
  identical input.
- **FR-013**: The guard MUST name the specific template responsible for any
  failure.
- **FR-014**: The deployed system MUST hold to the constitution's cost posture:
  compute scales to zero, the database uses the free serverless offer with
  auto-pause, and no new standing cost beyond the container registry is
  introduced.
- **FR-015**: When the budget backstop is skipped for missing configuration, the
  operator MUST be told explicitly.

#### Operability

- **FR-016**: The deployment platform MUST be configured to evaluate instance
  health by calling the application's own liveness and readiness endpoints.
- **FR-017**: Readiness MUST gate traffic — an instance that has started but is
  not ready MUST NOT receive requests.
- **FR-018**: Telemetry MUST be exported to a queryable destination whenever the
  environment supplies one.
- **FR-019**: The absence of a telemetry destination MUST NOT prevent the
  service from starting or serving.
- **FR-020**: Telemetry export failure MUST NOT cause request failure or
  instance unhealthiness.
- **FR-021**: Health probe configuration MUST be confirmed against a real
  deployment before being considered complete.

#### Validation

- **FR-022**: The cross-tenant attack suite MUST be executed against the
  deployed API and MUST pass in full.
- **FR-023**: Non-existence and non-membership MUST remain indistinguishable to
  a caller of the deployed API.
- **FR-024**: The CI pipeline MUST be demonstrated to **fail** on a broken test.
  (Execution on a real runner is already established by run 32537061874; that
  a green pipeline can actually go red is not.)
- **FR-025**: Validation results MUST be recorded durably enough to serve as the
  Principle IX evidence feature 001 depends on, including what was run, against
  what, and when.
- **FR-026**: Feature 001's task list MUST record that its validation gate is
  satisfied by this feature, and MUST NOT retain the migrated tasks.

### Key Entities

- **Identity tenant**: the external directory holding the people who can sign
  in, distinct from the directory owning the Azure subscription.
- **Application registration**: the identity system's record of Sprint Sync,
  source of the client and audience configuration values.
- **Deployment environment**: a named, reproducible target carrying its own
  configuration values and provisioned resources.
- **Deployment path**: a declared route by which an infrastructure template is
  actually applied. Templates not on any path are dead infrastructure.
- **Validation record**: durable evidence of what was tested, against which
  deployment, and with what result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real person can open the **hosted** web application at its public
  URL, sign in, and see their own organization — no local tooling involved.
- **SC-001a**: The hosted web application adds no standing cost.
- **SC-002**: An operator starting with no Azure or identity setup reaches a
  working deployment by following the documentation alone, without help from the
  implementers.
- **SC-003**: 100% of the cross-tenant attack suite passes against the deployed
  API, with zero cases where a cross-tenant response differs from a
  does-not-exist response.
- **SC-004**: The deployed system's idle cost stays within the constitution's
  stated floor, confirmed by observed spend over at least one full idle day.
- **SC-005**: The cost guard fails when given infrastructure belonging to no
  deployment path, and passes on the real infrastructure — verified by both
  implementations reaching the same verdict.
- **SC-006**: After exercising the deployed API, a trace for a specific request
  is queryable within 5 minutes.
- **SC-007**: The deployed service survives a full deployment cycle without
  crash-looping, and the platform withholds traffic from instances that are not
  ready.
- **SC-008**: A deliberately broken test makes CI fail, and CI still passes with
  this feature's changes applied.
- **SC-009**: Feature 001's "not validated" gap is closed by a dated record
  naming what ran, where, and with what result.

## Assumptions

- The operator has an Azure subscription with rights to create resources and to
  create an external-identity tenant. (Verified 2026-08-22: subscription
  `Personal Azure Subscription` is Enabled and both CLIs authenticate.)
- Real spend is acceptable. The constitution accepts a ~$5–8/month idle floor;
  this feature causes that floor to begin.
- The identity tenant will be newly created. The subscription's existing
  directory is not an external-identity tenant and is not suitable for
  customer-facing sign-in.
- A single deployment environment is sufficient. Separate staging and production
  environments are out of scope.
- Cold-start and database-resume latency after idle is accepted, per the
  constitution.
- The existing cross-tenant attack suite can be pointed at a deployed API
  without being rewritten; if it cannot, adapting it is in scope.
- Feature 001's application behavior is correct as built. This feature validates
  it, and changes it only where deployment requires (the CORS policy, FR-030).
- **Feature 001's T044 is falsely marked complete.** It claims "Configure React
  deployment to Azure Static Web Apps Free + `azd` wiring" is done, but no Static
  Web Apps configuration exists anywhere in the repository — no workflow, no
  Bicep, no azd service, no CLI config. `ci.yml`'s web job lints, tests and
  builds; it does not deploy. Web hosting is therefore **new work here**, not
  the re-validation of existing work, and 001's record must be corrected.
- The web application is a static SPA with no server-side rendering, so free
  static hosting is sufficient.

## Settled Decisions *(do not re-litigate)*

Established 2026-08-22 by direct verification against azd 1.31.2. These are
findings, not preferences — each was checked against the repository.

- **The per-service container app template is NOT orphaned.** It is the
  deploy-time template applied after the container image is built. Regenerating
  the infrastructure reproduces it byte-identically and deliberately leaves it
  out of the main provisioning template, because it takes the container image as
  a parameter — which cannot exist at provision time. The main template's seven
  outputs are precisely this template's remaining inputs. It MUST NOT be wired
  into the main template and MUST NOT be deleted. **The original handoff's
  "orphaned module" premise was disproved.**
- **The budget template is likewise deployed by a declared hook**, not by the
  main provisioning template.
- **Three legitimate deployment paths therefore exist**: provisioning,
  per-service deploy, and declared hook. "Not reachable from the main template"
  is **not** by itself a defect; "on none of the three paths" is. An earlier
  draft of the constitution amendment got this wrong and was corrected.
- **The four identity parameters declared in the main provisioning template are
  unused there.** This is generated, reproduces on regeneration, and is not to be
  "fixed" — identity configuration reaches the API at deploy time, not through
  provisioning.
- **The configuration guard already checks the correct value names.** That
  wiring is sound and needs no change.
- **Tooling is current**: regenerating from the previously-used version produced
  zero diff, so no version-drift investigation is warranted.

## Dependencies

- **Constitution v1.1.0** (PR #4, open at time of writing) adds Principle X
  (Operable by Default) and the three-deployment-path verification rule. Stories
  3 and 4 implement those rules; **PR #4 should merge before this feature's
  plan** so the Constitution Check evaluates against the rules the work is
  meant to satisfy.
- **Feature 001** must remain functionally unchanged. This feature validates it.
- **Already established, do not rebuild**: `.github/workflows/ci.yml` runs green
  on a real runner (run 32537061874, PR #3, all four jobs). Feature 001's Phase
  9A (migration lock, cancellation tokens, deepened contract test, query-filter
  refactor) is complete and merged.
- **Ordering dependency introduced by web hosting**: the hosted origin is not
  known until the hosting resource exists, but that origin is required by both
  the API's permitted-origin configuration (FR-030) and the identity redirect URI
  (FR-031); and the API's URL is required to build the SPA (FR-029). Hosting
  resource first, then configuration, then build and deploy.

## Out of Scope

- Any React application feature work. Hosting the SPA is **in** scope (see
  US1); changing what it does is not.
- Any change to feature 001's application behavior, **except** the CORS policy
  the API requires to accept browser requests from the hosted SPA's origin. That
  is deployment-enabling configuration, not a behavioural change, and without it
  a hosted SPA cannot call the API at all (see FR-030).
- Separate staging/production environments, custom domains, and TLS certificates
  beyond platform defaults.
- Autoscaling beyond the constitution's single-replica ceiling.
- Alerting and on-call routing. This feature establishes signal; acting on it is
  later work.
