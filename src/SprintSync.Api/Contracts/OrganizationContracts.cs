using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Contracts;

// The create-organization handler is the single, documented source of truth for
// name validation (trim, non-empty, ≤100 UTF-16 code units); no DataAnnotations
// here that would imply a validation filter that does not run (P2-16).

/// <summary>Request to create an organization (FR-001/012).</summary>
public sealed record CreateOrganizationRequest(string Name);

/// <summary>An organization the caller belongs to, with their role.</summary>
public sealed record OrganizationSummary(Guid Id, string Name, OrgRole Role);
