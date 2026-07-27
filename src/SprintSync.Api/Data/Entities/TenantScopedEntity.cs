namespace SprintSync.Api.Data.Entities;

/// <summary>
/// Base for every tenant-scoped ("work data") table. Subclasses carry
/// <see cref="OrganizationId"/> and automatically receive the global query
/// filter keyed to the ambient tenant (see <c>AppDbContext.OnModelCreating</c>).
///
/// No concrete subclass exists in this slice — the identity/control-plane
/// entities (User, Organization, Membership) are the Principle V carve-out.
/// This base is the seam the first real work-data entity will use.
/// </summary>
public abstract class TenantScopedEntity
{
    public Guid OrganizationId { get; set; }
}
