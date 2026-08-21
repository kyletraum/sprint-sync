using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data.Entities;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Organizations;

/// <summary>US1 / FR-001/002/003/012: create an organization and become Owner.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class CreateOrganizationTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_MakesCallerOwner_AndSetsActiveOrganization()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");

        var create = await client.PostAsJsonAsync("/api/v1/organizations", new { name = "Acme" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var org = await create.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options);
        Assert.NotNull(org);
        Assert.Equal("Acme", org!.Name);
        Assert.Equal(OrgRole.Owner, org.Role);
        Assert.NotEqual(Guid.Empty, org.Id);

        // The new org becomes the caller's active organization (FR-014).
        var me = await (await client.GetAsync("/api/v1/me"))
            .Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.Equal(org.Id, me!.ActiveOrganizationId);
    }

    [Fact]
    public async Task Create_WithBlankName_Returns400_AndCreatesNothing()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");

        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Still empty state — nothing was created.
        var me = await (await client.GetAsync("/api/v1/me"))
            .Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.Null(me!.ActiveOrganizationId);
    }

    // --- FR-012: the ≤100 boundary is a real, tested edge (P1-10) ----------

    [Fact]
    public async Task Create_WithMaxLengthName_Succeeds()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");
        var name = new string('a', 100);

        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var org = await response.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options);
        Assert.Equal(name, org!.Name);
    }

    [Fact]
    public async Task Create_WithOverLengthName_Returns400_WithNameError_AndCreatesNothing()
    {
        var client = _factory.CreateClientFor($"user-{Guid.NewGuid()}");
        var name = new string('a', 101);

        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The 400 contract carries a 'name' validation error (locks the shape).
        var problem = await response.Content
            .ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options);
        Assert.Contains(problem!.Errors.Keys, k => k.Equals("name", StringComparison.OrdinalIgnoreCase));

        var me = await (await client.GetAsync("/api/v1/me"))
            .Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.Null(me!.ActiveOrganizationId);
    }

    [Fact]
    public async Task Create_DuplicateName_YieldsTwoDistinctOrganizations()
    {
        // Names are for human recognition, not identity (spec assumption) — two
        // orgs may share a name and remain distinct (P2-13).
        var client = _factory.CreateClientFor($"dup-{Guid.NewGuid()}");

        var first = await Create(client, "Acme");
        var second = await Create(client, "Acme");
        Assert.NotEqual(first.Id, second.Id);

        var page = await (await client.GetAsync("/api/v1/organizations"))
            .Content.ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.Items.Select(o => o.Id).Distinct().Count());

        static async Task<OrganizationSummary> Create(HttpClient c, string name)
        {
            var r = await c.PostAsJsonAsync("/api/v1/organizations", new { name });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options))!;
        }
    }
}
