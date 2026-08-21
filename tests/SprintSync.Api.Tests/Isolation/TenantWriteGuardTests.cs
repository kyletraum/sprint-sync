using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Data;
using SprintSync.Api.Tenancy;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Isolation;

/// <summary>
/// P1-3 / Principle V on the WRITE path: the global query filter guards reads;
/// the SaveChanges guard stamps and enforces OrganizationId on writes, so a
/// cross-tenant or unscoped insert cannot reach the database — proven with the
/// same throwaway TenantScopedEntity the read-path filter tests use.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TenantWriteGuardTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = Context(new TenantContext());
        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID('TestScopedRows') IS NULL
            CREATE TABLE TestScopedRows (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                OrganizationId uniqueidentifier NOT NULL,
                Title nvarchar(200) NOT NULL);
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_WithUnsetOrg_IsStampedToTheAmbientTenant()
    {
        await using var db = Context(TenantFor(_orgA));
        db.ScopedRows.Add(new TestScopedRow { Id = Guid.NewGuid(), Title = "unset" });

        await db.SaveChangesAsync();

        var row = await db.ScopedRows.SingleAsync(); // filter scopes to _orgA
        Assert.Equal(_orgA, row.OrganizationId);
    }

    [Fact]
    public async Task Insert_ForADifferentOrg_IsRejected()
    {
        await using var db = Context(TenantFor(_orgA));
        db.ScopedRows.Add(new TestScopedRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgB, // not the ambient tenant
            Title = "cross-tenant",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Insert_WithNoAmbientTenant_IsRejected()
    {
        await using var db = Context(new TenantContext()); // no tenant resolved
        db.ScopedRows.Add(new TestScopedRow { Id = Guid.NewGuid(), Title = "no-tenant" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static TenantContext TenantFor(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return tenant;
    }

    private TestTenantDbContext Context(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(sql.ConnectionString)
            .Options;
        return new TestTenantDbContext(options, tenant);
    }
}
