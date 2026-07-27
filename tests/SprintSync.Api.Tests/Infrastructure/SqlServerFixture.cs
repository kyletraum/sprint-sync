using Testcontainers.MsSql;

namespace SprintSync.Api.Tests.Infrastructure;

/// <summary>
/// Shared SQL Server test container. Real SQL (not the in-memory provider) so
/// relational query filters and isolation behave faithfully (research R9).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

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
