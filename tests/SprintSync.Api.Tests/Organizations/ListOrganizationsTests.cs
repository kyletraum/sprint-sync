using System.Net;
using System.Net.Http.Json;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data.Entities;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Organizations;

/// <summary>
/// US2 / FR-005 / SC-003: a user lists exactly the organizations they belong to.
/// This is the sanctioned cross-organization read (Principle V carve-out), so
/// membership — not the ambient tenant — is what scopes it.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ListOrganizationsTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task List_ForNewUser_ReturnsEmptyEnvelope()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");

        var response = await client.GetAsync("/api/v1/organizations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.DefaultPageSize, page.PageSize);
    }

    [Fact]
    public async Task List_ReturnsExactlyTheCallersOrgs_AndExcludesOthers()
    {
        var member = _factory.CreateClientFor($"member-{Guid.NewGuid()}");
        var stranger = _factory.CreateClientFor($"stranger-{Guid.NewGuid()}");

        var mine = await CreateOrgAsync(member, "Alpha");
        var alsoMine = await CreateOrgAsync(member, "Beta");
        var theirs = await CreateOrgAsync(stranger, "Gamma");

        var page = await GetPageAsync(member);

        // Exactly N orgs, no more (SC-003).
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            new[] { mine.Id, alsoMine.Id }.OrderBy(id => id),
            page.Items.Select(o => o.Id).OrderBy(id => id));
        Assert.DoesNotContain(page.Items, o => o.Id == theirs.Id);

        // The stranger's view is disjoint — no leakage in either direction.
        var strangerPage = await GetPageAsync(stranger);
        Assert.Equal(theirs.Id, Assert.Single(strangerPage.Items).Id);
    }

    [Fact]
    public async Task List_CarriesTheCallersRoleInEachOrg()
    {
        var client = _factory.CreateClientFor($"owner-{Guid.NewGuid()}");
        await CreateOrgAsync(client, "Solo");

        var page = await GetPageAsync(client);

        Assert.Equal(OrgRole.Owner, Assert.Single(page.Items).Role);
    }

    [Fact]
    public async Task List_Paginates_WithStableOrderingAndTotalCount()
    {
        var client = _factory.CreateClientFor($"pager-{Guid.NewGuid()}");
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await CreateOrgAsync(client, name);
        }

        var first = await GetPageAsync(client, page: 1, pageSize: 2);
        var second = await GetPageAsync(client, page: 2, pageSize: 2);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(2, second.Page);

        // Pages partition the set — stable ordering, no overlap, no gaps.
        var paged = first.Items.Concat(second.Items).Select(o => o.Id).ToList();
        Assert.Equal(3, paged.Distinct().Count());
        Assert.Equal(new[] { "One", "Three", "Two" }, first.Items.Concat(second.Items).Select(o => o.Name));
    }

    [Fact]
    public async Task List_ClampsAndCoercesPagingBoundaries()
    {
        var client = _factory.CreateClientFor($"bounds-{Guid.NewGuid()}");
        await CreateOrgAsync(client, "Solo");

        // Over-cap pageSize is clamped (no unbounded Take).
        Assert.Equal(PageRequest.MaxPageSize, (await GetPageAsync(client, 1, 1000)).PageSize);

        // Non-positive page/pageSize coerce to defaults — never a negative Skip.
        Assert.Equal(1, (await GetPageAsync(client, 0, 10)).Page);
        Assert.Equal(1, (await GetPageAsync(client, -5, 10)).Page);
        Assert.Equal(PageRequest.DefaultPageSize, (await GetPageAsync(client, 1, 0)).PageSize);

        // A page past the end is an empty page, not an error, count intact.
        var beyond = await GetPageAsync(client, 99, 10);
        Assert.Empty(beyond.Items);
        Assert.Equal(1, beyond.TotalCount);
        Assert.Equal(99, beyond.Page);

        // An int-overflow-scale page coerces to an empty page rather than
        // wrapping into a negative SQL OFFSET and 500-ing (P1-3).
        var huge = await GetPageAsync(client, 30_000_000, 100);
        Assert.Empty(huge.Items);
        Assert.Equal(1, huge.TotalCount);
    }

    [Fact]
    public async Task List_WithoutAuthentication_Is401_WithProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/organizations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // UseStatusCodePages + AddProblemDetails render even the auth challenge as
        // RFC 9457 problem+json, so every error the client sees shares one shape
        // (Principle III). Pinned so the envelope is a decision, not incidental (P2-15).
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<OrganizationSummary> CreateOrgAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options))!;
    }

    private static async Task<PagedResult<OrganizationSummary>> GetPageAsync(
        HttpClient client, int? page = null, int? pageSize = null)
    {
        var query = page is null && pageSize is null
            ? string.Empty
            : $"?page={page ?? 1}&pageSize={pageSize ?? PageRequest.DefaultPageSize}";
        var response = await client.GetAsync($"/api/v1/organizations{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options))!;
    }
}
