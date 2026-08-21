using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Contracts;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Me;

/// <summary>
/// US3 / FR-006/007/014: choosing and switching the active organization, and
/// what happens when the persisted selection stops being valid. The stored
/// active org is a convenience, never an authority — every use is re-verified
/// against Membership, and a selection that no longer holds self-heals rather
/// than lingering.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ActiveOrganizationTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Switch_BetweenOwnOrgs_PersistsAcrossRequests()
    {
        var client = _factory.CreateClientFor($"switcher-{Guid.NewGuid()}");
        var acme = await CreateOrgAsync(client, "Acme");
        var beta = await CreateOrgAsync(client, "Beta");

        // The first org created became active (FR-014).
        Assert.Equal(acme.Id, await ActiveOrgAsync(client));

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = beta.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.Equal(beta.Id, body!.ActiveOrganizationId);

        // Persisted, not just echoed — a fresh request sees the new selection.
        Assert.Equal(beta.Id, await ActiveOrgAsync(client));

        // And switching back works just as well.
        await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = acme.Id });
        Assert.Equal(acme.Id, await ActiveOrgAsync(client));
    }

    [Fact]
    public async Task Switch_ToNonMemberOrg_Is404_AndLeavesSelectionUnchanged()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");
        var mine = await CreateOrgAsync(client, "Acme");

        var stranger = _factory.CreateClientFor($"stranger-{Guid.NewGuid()}");
        var theirs = await CreateOrgAsync(stranger, "Gamma");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = theirs.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(mine.Id, await ActiveOrgAsync(client));
    }

    [Fact]
    public async Task Switch_ToUnknownOrg_Is404_AndLeavesSelectionUnchanged()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");
        var mine = await CreateOrgAsync(client, "Acme");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(mine.Id, await ActiveOrgAsync(client));
    }

    // --- FR-014: a persisted selection that stopped being valid ------------

    [Fact]
    public async Task StaleActiveOrg_ResolvesToAnotherMembership_NeverTheStaleOne()
    {
        var externalId = $"stale-{Guid.NewGuid()}";
        var client = _factory.CreateClientFor(externalId);
        var acme = await CreateOrgAsync(client, "Acme");
        var beta = await CreateOrgAsync(client, "Beta");

        await SetActiveAsync(client, beta.Id);

        // Corrupt the stored selection to an org that does not exist at all —
        // the shape a deleted org or a bad write would leave behind.
        var ghost = Guid.NewGuid();
        await ForceActiveOrgAsync(externalId, ghost);

        var resolved = await ActiveOrgAsync(client);

        // Never the stale value — and the winner is deterministic, not incidental:
        // self-heal orders by (name, id), so "Acme" beats "Beta" every time (P1-8).
        Assert.NotEqual(ghost, resolved);
        Assert.Equal(acme.Id, resolved);
        Assert.NotEqual(beta.Id, resolved);

        // The repair is persisted, so the bad value does not resurface.
        Assert.Equal(resolved, await ActiveOrgAsync(client));
    }

    [Fact]
    public async Task StaleActiveOrg_WithNoRemainingMemberships_FallsBackToEmptyState()
    {
        var externalId = $"orphan-{Guid.NewGuid()}";
        var client = _factory.CreateClientFor(externalId);
        await client.GetAsync("/api/v1/me"); // JIT-provision, no memberships

        await ForceActiveOrgAsync(externalId, Guid.NewGuid());

        // Fail closed to the empty state rather than keeping a value that grants
        // nothing but looks like a selection.
        Assert.Null(await ActiveOrgAsync(client));
    }

    [Fact]
    public async Task StaleActiveOrg_GrantsNoAccessToTheStaleOrg()
    {
        var externalId = $"probe-{Guid.NewGuid()}";
        var client = _factory.CreateClientFor(externalId);
        await CreateOrgAsync(client, "Acme");

        var stranger = _factory.CreateClientFor($"stranger-{Guid.NewGuid()}");
        var theirs = await CreateOrgAsync(stranger, "Gamma");

        // Plant another user's org as the stored active org — the exact state an
        // attacker would want, reached by writing straight to the database.
        await ForceActiveOrgAsync(externalId, theirs.Id);

        // A persisted selection is not a grant (FR-010).
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/organizations/{theirs.Id}")).StatusCode);
        Assert.NotEqual(theirs.Id, await ActiveOrgAsync(client));

        var list = await client.GetAsync("/api/v1/organizations");
        var page = await list.Content
            .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
        Assert.DoesNotContain(page!.Items, o => o.Id == theirs.Id);
    }

    // --- helpers ----------------------------------------------------------

    private static async Task<OrganizationSummary> CreateOrgAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options))!;
    }

    private static async Task SetActiveAsync(HttpClient client, Guid organizationId)
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid?> ActiveOrgAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options))!
            .ActiveOrganizationId;
    }

    /// <summary>
    /// Writes an active-organization value the API would never accept, so the
    /// resolution path can be tested against states only corruption or a race
    /// could produce.
    /// </summary>
    private Task ForceActiveOrgAsync(string externalId, Guid organizationId) =>
        _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.ExternalId == externalId);
            user.ActiveOrganizationId = organizationId;
            await db.SaveChangesAsync();
        });
}
