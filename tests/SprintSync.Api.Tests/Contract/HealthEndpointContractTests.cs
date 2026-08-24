using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Contract;

/// <summary>
/// Constitution Principle X (Operable by Default) — the probe contract in
/// specs/002-azure-deploy-baseline/contracts/health-endpoints.md.
///
/// These run against a <b>Production</b>-hosted API on purpose. The failure they
/// exist to prevent only occurs in Production: <c>/health</c> is mapped only in
/// Development, so a readiness probe aimed at it 404s in Azure, every probe
/// fails, the revision never reports Ready, and the deployment hangs or rolls
/// back. A Development-hosted test sees <c>/health</c> respond and proves
/// nothing about the deployed system.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class HealthEndpointContractTests(SqlServerFixture sql) : IDisposable
{
    private readonly ProductionApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Liveness_IsExposedInProduction()
    {
        var response = await _factory.CreateClient().GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_IsExposedInProduction_AndReportsReadyAfterStartup()
    {
        // CreateClient() starts the host, which runs migration and then flips the
        // startup gate — so by the time a request can be issued, readiness is up.
        var response = await _factory.CreateClient().GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DetailedHealth_Is404InProduction()
    {
        // Deliberate: /health returns per-check detail and stays Development-only
        // (https://aka.ms/aspire/healthchecks). This assertion is the tripwire for
        // anyone "fixing" the 404 by mapping it in Production, or pointing the ACA
        // readiness probe here instead of at /ready.
        var response = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_LeaksNoPerCheckDetail()
    {
        // What makes /ready safe in Production where /health is not: status only,
        // no check names, no dependency names, no exception text.
        var body = await _factory.CreateClient().GetStringAsync("/ready");

        Assert.DoesNotContain("startup", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("self", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeEndpoints_RequireNoAuthentication()
    {
        // ACA probes are unauthenticated. If either endpoint began demanding a
        // token, every probe would fail and the revision would never go Ready.
        var client = _factory.CreateClient();

        foreach (var path in new[] { "/alive", "/ready" })
        {
            var response = await client.GetAsync(path);

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}

/// <summary>
/// The readiness gate's own behaviour, independent of hosting: it must report
/// not-ready until startup work completes, and it must never touch the database.
/// </summary>
public sealed class StartupGateTests
{
    [Fact]
    public void Gate_IsNotReady_BeforeStartupCompletes()
    {
        var gate = new StartupGate();

        Assert.False(gate.IsReady);
    }

    [Fact]
    public void Gate_BecomesReady_OnceMarked()
    {
        var gate = new StartupGate();

        gate.MarkReady();

        Assert.True(gate.IsReady);
    }

    [Fact]
    public void MarkReady_IsIdempotent()
    {
        var gate = new StartupGate();

        gate.MarkReady();
        gate.MarkReady();

        Assert.True(gate.IsReady);
    }

    [Fact]
    public async Task HealthCheck_ReportsUnhealthyUntilReady_ThenHealthy()
    {
        var gate = new StartupGate();
        var check = new StartupGateHealthCheck(gate);
        var context = new HealthCheckContext();

        var before = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Unhealthy, before.Status);

        gate.MarkReady();

        var after = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }
}
