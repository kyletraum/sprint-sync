namespace SprintSync.Api.Data.Entities;

/// <summary>
/// The sole authority for what a user may see or do within an organization.
/// Carries <see cref="OrganizationId"/> for the join but is part of the
/// identity/control-plane carve-out — it is NOT globally filtered, because
/// "list my orgs" and membership verification are cross-organization reads
/// (Principle V carve-out).
/// </summary>
public class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public OrgRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
