namespace SprintSync.Api.Data.Entities;

/// <summary>
/// The application's local mirror of an externally-authenticated identity.
/// Created just-in-time on first authenticated request (FR-013). Control-plane:
/// a user spans organizations, so it is never tenant-filtered.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Stable external identity (Entra External ID <c>oid</c> claim).</summary>
    public required string ExternalId { get; set; }

    /// <summary>Cached from the token for convenience; not authoritative.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Persisted active-organization selection (FR-014). Re-verified against
    /// <see cref="Membership"/> on every use — never grants access on its own.
    /// </summary>
    public Guid? ActiveOrganizationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
