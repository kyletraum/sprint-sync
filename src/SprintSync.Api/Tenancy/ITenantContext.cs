namespace SprintSync.Api.Tenancy;

/// <summary>
/// Holds the server-resolved, membership-verified ambient organization for the
/// current request (Principle VI). Populated only by
/// <see cref="TenantResolutionMiddleware"/> — never from a raw client value.
/// The global query filter on tenant-scoped entities reads
/// <see cref="OrganizationId"/>.
/// </summary>
public interface ITenantContext
{
    Guid? OrganizationId { get; }

    bool HasTenant => OrganizationId.HasValue;

    /// <summary>Set the verified ambient tenant. Middleware-only.</summary>
    void SetOrganization(Guid? organizationId);
}

public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public void SetOrganization(Guid? organizationId) => OrganizationId = organizationId;
}
