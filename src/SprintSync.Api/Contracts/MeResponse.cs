namespace SprintSync.Api.Contracts;

/// <summary>Current user + active organization (null when none — empty state).</summary>
public sealed record MeResponse(Guid UserId, string? DisplayName, Guid? ActiveOrganizationId);
