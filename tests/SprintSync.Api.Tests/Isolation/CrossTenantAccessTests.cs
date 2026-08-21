using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data.Entities;
using SprintSync.Api.Tenancy;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Isolation;

/// <summary>
/// US4 / FR-007/008/009/010 / SC-002: every route to an organization the caller
/// does not belong to is indistinguishable from that organization not existing,
/// and the acting context is always the server's verified record — never a
/// value the client supplied.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CrossTenantAccessTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    // --- FR-008/009: hide-existence on read -------------------------------

    [Fact]
    public async Task Get_NonMemberOrg_IsByteIdenticalToUnknownId()
    {
        var (outsider, _) = await NewUserWithOrgAsync("outsider", "Acme");
        var (_, theirs) = await NewUserWithOrgAsync("insider", "Gamma");

        var nonMember = await outsider.GetAsync($"/api/v1/organizations/{theirs.Id}");
        var unknown = await outsider.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, nonMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Byte-identical body: nothing distinguishes "exists but not yours" from
        // "does not exist" (SC-002). A differing detail, title or trace value
        // would be an enumeration oracle.
        var nonMemberBody = await nonMember.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.Equal(unknownBody, nonMemberBody);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            nonMember.Content.Headers.ContentType?.ToString());

        // Both are the uniform ProblemDetails (Principle III) — not the empty
        // body a missing route would produce, which would make the equality
        // above pass for the wrong reason.
        Assert.Equal("application/problem+json", nonMember.Content.Headers.ContentType?.MediaType);
        var problem = await nonMember.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Equal(StatusCodes.Status404NotFound, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));

        // The body names nothing about the org that was probed.
        Assert.DoesNotContain(theirs.Id.ToString(), nonMemberBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_OwnOrg_Succeeds()
    {
        var (client, mine) = await NewUserWithOrgAsync("owner", "Acme");

        var response = await client.GetAsync($"/api/v1/organizations/{mine.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<OrganizationDetail>(TestJson.Options);
        Assert.Equal(mine.Id, detail!.Id);
        Assert.Equal("Acme", detail.Name);
        Assert.Equal(OrgRole.Owner, detail.Role);
        Assert.Equal(1, detail.MemberCount);
    }

    // --- FR-007: hide-existence on act ------------------------------------

    [Fact]
    public async Task SetActiveOrganization_ToNonMemberOrg_Is404_AndLeavesSelectionUnchanged()
    {
        var (attacker, mine) = await NewUserWithOrgAsync("attacker", "Acme");
        var (_, theirs) = await NewUserWithOrgAsync("victim", "Gamma");

        var response = await attacker.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = theirs.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The previous selection survives the failed attempt (FR-007).
        Assert.Equal(mine.Id, await ActiveOrgAsync(attacker));

        // ...and the refusal is indistinguishable from an unknown id — the same
        // uniform ProblemDetails the read path returns.
        var unknown = await attacker.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = Guid.NewGuid() });
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await response.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var read = await attacker.GetAsync($"/api/v1/organizations/{theirs.Id}");
        Assert.Equal(
            await read.Content.ReadAsStringAsync(),
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetActiveOrganization_ToOwnOrg_Switches()
    {
        var (client, first) = await NewUserWithOrgAsync("switcher", "Acme");
        var second = await CreateOrgAsync(client, "Beta");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/active-organization", new { organizationId = second.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response echoes the new state (contract: 200 + MeResponse)...
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options);
        Assert.Equal(second.Id, body!.ActiveOrganizationId);

        // ...and it is genuinely persisted, not just reflected back.
        Assert.Equal(second.Id, await ActiveOrgAsync(client));
        Assert.NotEqual(first.Id, second.Id);
    }

    // --- FR-010: the client never sets the acting context -----------------

    [Fact]
    public async Task OrganizationHeader_ForNonMemberOrg_IsIgnored_AndGrantsNothing()
    {
        var (attacker, mine) = await NewUserWithOrgAsync("spoofer", "Acme");
        var (_, theirs) = await NewUserWithOrgAsync("target", "Gamma");

        attacker.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.OrganizationHeader, theirs.Id.ToString());

        // The spoofed id never becomes the acting context.
        Assert.NotEqual(theirs.Id, await AmbientTenantAsync(attacker));

        // And it grants no access to the spoofed org by any route.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await attacker.GetAsync($"/api/v1/organizations/{theirs.Id}")).StatusCode);

        var list = await attacker.GetAsync("/api/v1/organizations");
        var page = await list.Content
            .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
        Assert.Equal(mine.Id, Assert.Single(page!.Items).Id);
    }

    [Fact]
    public async Task OrganizationHeader_ForMemberOrg_SetsTheActingContext()
    {
        var (client, _) = await NewUserWithOrgAsync("member", "Acme");
        var second = await CreateOrgAsync(client, "Beta");

        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.OrganizationHeader, second.Id.ToString());

        // Positive control: the header is honoured — but only after the server
        // verified the membership itself.
        Assert.Equal(second.Id, await AmbientTenantAsync(client));
    }

    // --- FR-010: revoked membership denies the next request ---------------

    [Fact]
    public async Task RevokedMembership_DeniesTheVeryNextRequest()
    {
        var (owner, org) = await NewUserWithOrgAsync("org-owner", "Acme");
        var guestExternalId = $"guest-{Guid.NewGuid()}";
        var guest = _factory.CreateClientFor(guestExternalId);

        // Provision the guest, then grant membership out of band (no invite
        // endpoint exists in this slice).
        await guest.GetAsync("/api/v1/me");
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.ExternalId == guestExternalId);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OrganizationId = org.Id,
                Role = OrgRole.Owner, // only role in this slice; membership existence is what is under test
                CreatedAt = DateTimeOffset.UtcNow,
            });
            user.ActiveOrganizationId = org.Id;
            await db.SaveChangesAsync();
        });

        Assert.Equal(
            HttpStatusCode.OK,
            (await guest.GetAsync($"/api/v1/organizations/{org.Id}")).StatusCode);

        // Revoke it.
        await _factory.WithDbAsync(async db =>
        {
            var membership = await db.Memberships.SingleAsync(m =>
                m.OrganizationId == org.Id && m.User.ExternalId == guestExternalId);
            db.Memberships.Remove(membership);
            await db.SaveChangesAsync();
        });

        // The very next request is denied — membership is re-verified per request,
        // and the stale persisted selection self-heals to the empty state (FR-014).
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await guest.GetAsync($"/api/v1/organizations/{org.Id}")).StatusCode);
        Assert.Null(await ActiveOrgAsync(guest));
        Assert.Null(await AmbientTenantAsync(guest));

        // The owner is untouched.
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.GetAsync($"/api/v1/organizations/{org.Id}")).StatusCode);
    }

    // --- FR-009: no timing signal (T034b regression guard) ----------------

    [Fact]
    [Trait("Category", "Timing")] // wall-clock; excludable from the gating run (P1-11)
    public async Task NonMemberAndUnknownId_HaveNoTimingDifference()
    {
        var (client, _) = await NewUserWithOrgAsync("timer", "Acme");
        var (_, theirs) = await NewUserWithOrgAsync("other", "Gamma");

        var nonMemberUrl = $"/api/v1/organizations/{theirs.Id}";

        // Warm up JIT, connection pool and query plans before measuring.
        for (var i = 0; i < 10; i++)
        {
            await client.GetAsync(nonMemberUrl);
            await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}");
        }

        const int Samples = 30;
        var nonMember = new List<double>(Samples);
        var unknown = new List<double>(Samples);

        // Interleaved so machine-level drift hits both series equally.
        for (var i = 0; i < Samples; i++)
        {
            nonMember.Add(await TimeAsync(client, nonMemberUrl));
            unknown.Add(await TimeAsync(client, $"/api/v1/organizations/{Guid.NewGuid()}"));
        }

        var a = Median(nonMember);
        var b = Median(unknown);

        // The two cases share one query and one response path, so any real
        // divergence means a branch or extra round-trip was reintroduced. The
        // band is deliberately wide: this guards structure, not microseconds
        // (research R4).
        var tolerance = Math.Max(5.0, 0.6 * Math.Max(a, b));
        Assert.True(
            Math.Abs(a - b) <= tolerance,
            $"Timing diverged: non-member median {a:F2}ms vs unknown-id median {b:F2}ms " +
            $"(tolerance {tolerance:F2}ms) — a structural difference may have been introduced.");
    }

    // --- Principles V/VI: isolation holds under concurrent HTTP load --------

    [Fact]
    public async Task ConcurrentTwoTenantRequests_EachSeeOnlyTheirOwnOrg()
    {
        var (a, aOrg) = await NewUserWithOrgAsync("tenant-a", "Acme");
        var (b, bOrg) = await NewUserWithOrgAsync("tenant-b", "Beta");

        // Interleave many concurrent list requests from both tenants against the
        // single scoped (non-pooled) context registration; every response must be
        // scoped to its own caller with zero cross-tenant bleed — the property the
        // EF-layer QueryFilterTests prove in the small, exercised here over HTTP.
        async Task AssertScoped(HttpClient client, Guid ownOrg)
        {
            var list = await client.GetAsync("/api/v1/organizations");
            list.EnsureSuccessStatusCode();
            var page = await list.Content
                .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
            Assert.Equal(ownOrg, Assert.Single(page!.Items).Id);
        }

        var work = Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? AssertScoped(a, aOrg.Id) : AssertScoped(b, bOrg.Id));
        await Task.WhenAll(work);
    }

    // --- FR-010: malformed client input grants nothing ---------------------

    [Fact]
    public async Task GarbageOrganizationHeader_IsIgnored_AndFallsBackToOwnActiveOrg()
    {
        var (client, mine) = await NewUserWithOrgAsync("garbled", "Acme");

        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.OrganizationHeader, "not-a-guid");

        // An unparseable header is not an error and confers nothing: the acting
        // context is simply the caller's own verified active org (FR-010).
        Assert.Equal(mine.Id, await AmbientTenantAsync(client));

        var list = await client.GetAsync("/api/v1/organizations");
        var page = await list.Content
            .ReadFromJsonAsync<PagedResult<OrganizationSummary>>(TestJson.Options);
        Assert.Equal(mine.Id, Assert.Single(page!.Items).Id);
    }

    // --- helpers ----------------------------------------------------------

    private static async Task<double> TimeAsync(HttpClient client, string url)
    {
        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync(url);
        sw.Stop();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }

    private async Task<(HttpClient Client, OrganizationSummary Org)> NewUserWithOrgAsync(
        string prefix, string orgName)
    {
        var client = _factory.CreateClientFor($"{prefix}-{Guid.NewGuid()}");
        return (client, await CreateOrgAsync(client, orgName));
    }

    private static async Task<OrganizationSummary> CreateOrgAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrganizationSummary>(TestJson.Options))!;
    }

    private static async Task<Guid?> ActiveOrgAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options))!
            .ActiveOrganizationId;
    }

    private static async Task<Guid?> AmbientTenantAsync(HttpClient client)
    {
        var response = await client.GetAsync(TenantProbeStartupFilter.Path);
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<TenantProbeResponse>(TestJson.Options))!.OrganizationId;
    }
}
