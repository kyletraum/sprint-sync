using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Contract;

/// <summary>
/// Constitution Principle X — the probe paths must not be exported as traces.
///
/// Why this exists (S6, committee round 1-2): <c>/ready</c> was added by feature
/// 002 and was left out of the OpenTelemetry filter that already excluded
/// <c>/health</c> and <c>/alive</c>. It is the worst one to miss — the container
/// app polls readiness at <c>periodSeconds: 5</c> against liveness at 30, so it
/// is six times the span volume of the probe that WAS excluded: roughly 720
/// spans/hour per warm replica, all shipped by <c>UseAzureMonitor()</c>.
///
/// The failure mode is silent and billable. Nothing breaks, no test goes red,
/// and the only symptom is that App Insights transaction search fills with probe
/// traffic and real request traces become unfindable — discovered on a bill.
/// That is the same argument that justifies the idle-cost guard, so the rule
/// gets a test rather than a comment.
///
/// Asserted against an <b>in-memory exporter</b>, not an ActivityListener, on
/// purpose. The filter does not prevent an Activity from being created — ASP.NET
/// Core still creates one — it prevents it from being sampled and exported. A
/// listener would therefore see the probe activities and this test would fail
/// for the wrong reason. What is exported is also exactly what
/// <c>UseAzureMonitor()</c> would ship, which is the property under test.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ProbeTracingTests : IDisposable
{
    private readonly List<Activity> _exported = [];
    private readonly ProductionApiFactory _factory;

    public ProbeTracingTests(SqlServerFixture sql) =>
        _factory = new ProductionApiFactory(
            sql.ConnectionString,
            services => services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddInMemoryExporter(_exported)));

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ProbeRequests_AreNotExported_WhileRealRequestsAre()
    {
        var client = _factory.CreateClient();

        // The positive control. 401 is fine — it only has to be traced.
        await client.GetAsync("/api/v1/me");

        await client.GetAsync("/alive");
        await client.GetAsync("/ready");
        await client.GetAsync("/health");

        _factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var paths = _exported.Select(PathOf).Where(p => p is not null).ToList();

        // POSITIVE CONTROL, asserted FIRST and deliberately.
        //
        // Without it this test passes when the tracing pipeline exports nothing
        // at all — a change that broke instrumentation outright would look like
        // a pass, and the three DoesNotContain assertions below would be
        // vacuous. That is precisely the defect the committee found in the
        // /ready contract tests (S4), where deleting the readiness check left
        // every assertion green. If this line fails, the rest proves nothing.
        Assert.Contains("/api/v1/me", paths);

        Assert.DoesNotContain("/alive", paths);
        Assert.DoesNotContain("/ready", paths);
        Assert.DoesNotContain("/health", paths);
    }

    /// <summary>
    /// The request path off an exported span. Tag name depends on which
    /// semantic-convention mode the instrumentation is in, so try each rather
    /// than pinning one and silently reading null — a null path would drop the
    /// span from the list and weaken every assertion above.
    /// </summary>
    private static string? PathOf(Activity activity) =>
        activity.GetTagItem("url.path") as string
        ?? activity.GetTagItem("http.target") as string
        ?? activity.GetTagItem("http.route") as string;
}
