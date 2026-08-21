using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Data.Entities;
using SprintSync.Api.Tenancy;

namespace SprintSync.Api.Data;

/// <summary>
/// The single data-access context. Registered scoped (NOT pooled) so the global
/// query filter reads the correct per-request <see cref="ITenantContext"/>
/// (research R2). Identity/control-plane entities (User, Organization,
/// Membership) are intentionally unfiltered (Principle V carve-out).
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>
    /// Ambient tenant read by the global query filter. Exposed as a context
    /// property so EF re-evaluates it against the current (scoped) context
    /// instance on every query — the supported dynamic-filter pattern.
    /// </summary>
    public Guid? CurrentOrganizationId => _tenant.OrganizationId;

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAndGuardTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAndGuardTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Write-path counterpart to the global query filter (Principle V): global
    /// query filters guard reads only, so without this a Guid.Empty or
    /// cross-tenant OrganizationId could be written by discipline-free handler
    /// code. Every added, modified, OR DELETED <see cref="TenantScopedEntity"/>
    /// is checked against the ambient tenant (and an unset org stamped on insert),
    /// and rejected if it targets any other org — a cross-tenant DELETE via a
    /// detached PK stub bypasses the read filter, so the guard must cover deletes
    /// too (P0-2). This closes the isolation seam symmetrically before the first
    /// work-data entity exists.
    /// </summary>
    private void StampAndGuardTenant()
    {
        foreach (var entry in ChangeTracker.Entries<TenantScopedEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var ambient = _tenant.OrganizationId
                ?? throw new InvalidOperationException(
                    "Refusing to write a tenant-scoped entity with no ambient tenant resolved (Principle VI).");

            // Stamp an unset org on INSERT only; never rewrite an org on update/delete.
            if (entry.State == EntityState.Added && entry.Entity.OrganizationId == Guid.Empty)
            {
                entry.Entity.OrganizationId = ambient;
            }

            if (entry.Entity.OrganizationId != ambient)
            {
                throw new InvalidOperationException(
                    $"Refusing to write a tenant-scoped entity for organization {entry.Entity.OrganizationId} " +
                    $"under ambient tenant {ambient} (Principle V — no cross-tenant writes).");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.ExternalId).HasMaxLength(200).IsRequired();
            b.HasIndex(u => u.ExternalId).IsUnique();
            b.Property(u => u.DisplayName).HasMaxLength(200);
            b.HasMany(u => u.Memberships).WithOne(m => m.User)
                .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Organization>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(100).IsRequired();
            b.HasMany(o => o.Memberships).WithOne(m => m.Organization)
                .HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasIndex(m => new { m.UserId, m.OrganizationId }).IsUnique();
            b.HasIndex(m => m.OrganizationId);
        });

        // Apply the tenant global query filter to every TenantScopedEntity
        // subclass. None exist in this slice; this is the seam future work-data
        // entities inherit (research R2). Membership carries OrganizationId but
        // is NOT a TenantScopedEntity, so it is deliberately unfiltered.
        //
        // Materialized first: the loop body adds filters to the model it is
        // walking.
        var scopedTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(TenantScopedEntity).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => entityType.ClrType)
            .ToList();

        foreach (var clrType in scopedTypes)
        {
            // Generic dispatch only — the predicate itself is the ordinary C#
            // lambda in ApplyTenantFilter, not a hand-built expression tree.
            ApplyTenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(
            nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// The tenant filter for one <see cref="TenantScopedEntity"/> subclass:
    /// <c>e.OrganizationId == CurrentOrganizationId</c>, with no ambient tenant
    /// therefore matching no rows (fail closed).
    ///
    /// <see cref="CurrentOrganizationId"/> is read off the context instance —
    /// EF Core's documented dynamic-filter pattern, and the reason the filter
    /// re-evaluates per context instance instead of baking the first request's
    /// organization into the cached model. QueryFilterTests holds that property.
    /// </summary>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantScopedEntity =>
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
}
