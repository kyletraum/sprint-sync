using System.ComponentModel.DataAnnotations;
using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Contracts;

/// <summary>Request to create an organization (FR-001/012).</summary>
public sealed record CreateOrganizationRequest(
    [property: Required, MinLength(1), MaxLength(100)] string Name);

/// <summary>An organization the caller belongs to, with their role.</summary>
public sealed record OrganizationSummary(Guid Id, string Name, OrgRole Role);
