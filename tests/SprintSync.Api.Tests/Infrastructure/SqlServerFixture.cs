using Testcontainers.MsSql;

namespace SprintSync.Api.Tests.Infrastructure;

/// <summary>
/// Shared SQL Server test container. Real SQL (not the in-memory provider) so
/// relational query filters and isolation behave faithfully (research R9).
///
/// CONVENTION (P2-16): the database is shared across the whole "sql" collection,
/// so every test MUST scope its data by a unique external id and/or org name
/// (e.g. $"user-{Guid.NewGuid()}") — never a fixed literal — so tests stay
/// independent as coverage grows.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Pin the engine so the harness's database is a declared, reproducible part
    // of the test — not whatever the transitive default resolves to (P2-15).
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql";
}
