using System.Net;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Auth;

/// <summary>
/// P1-9 / research R4: the OrgMember policy's denial is rendered as the uniform
/// hide-existence 404 by the custom authorization result handler — never a 403
/// that would confirm the resource exists. Exercised via a test-only
/// OrgMember-protected endpoint (Program maps it only when TestEndpoints:Enabled).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class HideExistencePolicyTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task OrgMemberDenial_ForCallerWithNoTenant_Returns404_ByteIdenticalToHideExistence()
    {
        // Authenticated, but belongs to no org -> no verified ambient tenant ->
        // the OrgMember requirement fails.
        var client = _factory.CreateClientFor($"no-tenant-{Guid.NewGuid()}");

        var denied = await client.GetAsync("/api/v1/_test/org-scoped");

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("application/problem+json", denied.Content.Headers.ContentType?.MediaType);

        // Byte-identical to the body a genuine hide-existence miss returns, so the
        // auth layer leaks no more than the data layer does.
        var reference = await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}");
        Assert.Equal(
            await reference.Content.ReadAsStringAsync(),
            await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OrgMemberEndpoint_Unauthenticated_Returns401_NotHiddenAs404()
    {
        // No authentication at all is a 401 (a challenge), distinct from the
        // tenant hide-existence 404 (an authorization denial) — the two failure
        // modes stay correctly separated.
        var response = await _factory.CreateClient().GetAsync("/api/v1/_test/org-scoped");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
