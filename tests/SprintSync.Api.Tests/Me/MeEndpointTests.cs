using System.Net;
using System.Net.Http.Json;
using SprintSync.Api.Contracts;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Me;

/// <summary>US1 / FR-013: JIT provisioning and the empty-state contract.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class MeEndpointTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetMe_NewUser_JitProvisions_WithEmptyState()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}", "Ada Lovelace");

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.NotNull(me);
        Assert.NotEqual(Guid.Empty, me!.UserId);
        Assert.Equal("Ada Lovelace", me.DisplayName);
        Assert.Null(me.ActiveOrganizationId); // belongs to no org yet
    }

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient(); // no X-Test-User header

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
