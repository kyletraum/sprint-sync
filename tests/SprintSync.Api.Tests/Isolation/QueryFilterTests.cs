using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Data;
using SprintSync.Api.Data.Entities;
using SprintSync.Api.Tenancy;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Isolation;

/// <summary>
/// Proof that the persistence-layer mechanism itself denies cross-org rows
/// (research R2/R9). No concrete <see cref="TenantScopedEntity"/> ships in this
/// slice, so this exercises the seam with a throwaway one: whatever the first
/// real work-data entity turns out to be, it inherits exactly this behaviour.
///
/// The load-bearing assertion is <see cref="Filter_IsReEvaluatedPerContextInstance"/>
/// — EF caches the model once per context type, so a filter that captured its
/// tenant at model-build time would serve the first request's org to every
/// later one. That is the pooling/staleness hazard Principle VI warns about.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class QueryFilterTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private Guid _rowA;
    private Guid _rowB;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext(new TenantContext());
        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID('TestScopedRows') IS NULL
            CREATE TABLE TestScopedRows (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                OrganizationId uniqueidentifier NOT NULL,
                Title nvarchar(200) NOT NULL);
            """);

        _rowA = await SeedAsync(db, _orgA, "row-in-a");
        _rowB = await SeedAsync(db, _orgB, "row-in-b");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Filter_ReturnsOnlyAmbientOrgRows()
    {
        await using var db = CreateContext(TenantFor(_orgA));

        var rows = await db.ScopedRows.ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(_rowA, row.Id);
        Assert.Equal(_orgA, row.OrganizationId);
    }

    [Fact]
    public async Task Filter_DeniesCrossOrgRow_EvenWhenFetchedByPrimaryKey()
    {
        await using var db = CreateContext(TenantFor(_orgA));

        // Knowing the exact id of another org's row buys the caller nothing —
        // isolation is not a matter of which predicate the handler remembered
        // to write (Principle V).
        var byId = await db.ScopedRows.FirstOrDefaultAsync(r => r.Id == _rowB);

        Assert.Null(byId);
    }

    [Fact]
    public async Task Filter_WithNoAmbientTenant_ReturnsNothing()
    {
        await using var db = CreateContext(new TenantContext());

        // Fail closed: an unresolved tenant yields no rows, never all rows.
        Assert.Empty(await db.ScopedRows.ToListAsync());
    }

    [Fact]
    public async Task Filter_IsReEvaluatedPerContextInstance()
    {
        await using (var asOrgA = CreateContext(TenantFor(_orgA)))
        {
            Assert.Equal(_rowA, (await asOrgA.ScopedRows.SingleAsync()).Id);
        }

        // A second context, a different tenant. If the filter had baked the
        // first instance into the cached model, this would still return org A.
        await using var asOrgB = CreateContext(TenantFor(_orgB));
        Assert.Equal(_rowB, (await asOrgB.ScopedRows.SingleAsync()).Id);
    }

    [Fact]
    public async Task Filter_SurvivesTenantChangeWithinOneContext()
    {
        var tenant = TenantFor(_orgA);
        await using var db = CreateContext(tenant);

        Assert.Equal(_rowA, (await db.ScopedRows.SingleAsync()).Id);

        // The filter reads the tenant at query time, not at construction time.
        tenant.SetOrganization(_orgB);
        Assert.Equal(_rowB, (await db.ScopedRows.SingleAsync()).Id);
    }

    private static async Task<Guid> SeedAsync(TestTenantDbContext db, Guid organizationId, string title)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO TestScopedRows (Id, OrganizationId, Title) VALUES ({0}, {1}, {2})",
            id, organizationId, title);
        return id;
    }

    private static TenantContext TenantFor(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return tenant;
    }

    private TestTenantDbContext CreateContext(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(sql.ConnectionString)
            .Options;
        return new TestTenantDbContext(options, tenant);
    }
}

/// <summary>
/// A throwaway work-data entity — the seam's first tenant.
///
/// Its backing <c>TestScopedRows</c> table is shared across the "sql" collection
/// and never dropped; that is safe ONLY because every assertion is scoped to a
/// unique per-test <c>OrganizationId</c>, so another test's rows are invisible
/// through the tenant filter. Keep new tests org-scoped (P2-9).
/// </summary>
public sealed class TestScopedRow : TenantScopedEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Derives from the real <see cref="AppDbContext"/> so the entity picks up the
/// production filter loop verbatim — the mechanism under test is the shipped
/// one, not a re-implementation.
/// </summary>
public sealed class TestTenantDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : AppDbContext(options, tenant)
{
    public DbSet<TestScopedRow> ScopedRows => Set<TestScopedRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Registered before base runs, so the base's TenantScopedEntity sweep
        // sees it and applies the global query filter.
        modelBuilder.Entity<TestScopedRow>(b =>
        {
            b.ToTable("TestScopedRows");
            b.HasKey(e => e.Id);
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
