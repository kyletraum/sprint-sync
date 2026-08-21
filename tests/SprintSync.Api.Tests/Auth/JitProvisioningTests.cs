using System.Net;
using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Auth;

/// <summary>P0-1 / FR-013: JIT provisioning is race-safe under a concurrent first login.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class JitProvisioningTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ConcurrentFirstRequests_ForSameIdentity_AllSucceed_AndCreateExactlyOneUser()
    {
        var externalId = $"race-{Guid.NewGuid()}";

        // A brand-new identity firing several first requests at once — the SPA's
        // GET /me + GET /organizations (and retries) on load. Every request hits
        // JIT provisioning with a null read; the losers of the unique-index race
        // must recover to the committed row, not surface a 500.
        var clients = Enumerable.Range(0, 8)
            .Select(_ => _factory.CreateClientFor(externalId))
            .ToArray();

        var responses = await Task.WhenAll(clients.Select((c, i) =>
            c.GetAsync(i % 2 == 0 ? "/api/v1/me" : "/api/v1/organizations")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Exactly one row exists for the identity — no duplicate, no failure.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.Users.CountAsync(u => u.ExternalId == externalId)));
    }
}
