namespace SprintSync.Api.Contracts;

/// <summary>
/// Request to change the caller's active organization (FR-006). The id is a
/// client-supplied value and never grants access on its own — it is honored only
/// after the server verifies a Membership (FR-010).
/// </summary>
public sealed record SetActiveOrganizationRequest(Guid OrganizationId);
