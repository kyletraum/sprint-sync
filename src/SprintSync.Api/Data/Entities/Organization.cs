namespace SprintSync.Api.Data.Entities;

/// <summary>
/// A tenant boundary and the root all org-scoped data will hang from. Its
/// <see cref="Id"/> is the discriminator value tenant-scoped tables carry as
/// <c>OrganizationId</c>. Not globally filtered — access to a specific
/// organization is gated by <see cref="Membership"/> (Principle V).
/// </summary>
public class Organization
{
    public Guid Id { get; set; }

    /// <summary>Non-empty, non-whitespace, max 100 chars (FR-012). Not globally unique.</summary>
    public required string Name { get; set; }

    /// <summary>The founding Owner.</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
