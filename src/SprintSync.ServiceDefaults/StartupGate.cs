using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Readiness signal for the deployed service (constitution Principle X).
///
/// Reports <b>not ready</b> from process start until <see cref="MarkReady"/> is
/// called once startup work — schema migration and seeding — has finished. During
/// that window the process is listening but cannot serve, which is precisely the
/// state readiness exists to describe, and the platform must withhold traffic
/// rather than restart the container.
///
/// <para>
/// <b>This deliberately performs no database access, and must not be changed to.</b>
/// The database is Azure SQL free serverless with a 60-second auto-pause delay. A
/// readiness check that opened a connection on every probe would keep it awake
/// permanently, breaching the constitution's Deployment &amp; Cost Constraints —
/// silently, and surfacing as a bill rather than as a failure. An in-memory flag
/// costs nothing per probe and is more truthful about what readiness means here.
/// See specs/002-azure-deploy-baseline/research.md R3.
/// </para>
/// </summary>
public sealed class StartupGate
{
    private volatile bool _ready;

    /// <summary>Whether startup work has completed and the service can serve requests.</summary>
    public bool IsReady => _ready;

    /// <summary>
    /// Signals that startup work has finished. Idempotent. There is no counterpart
    /// that un-readies the service: a process that has become unhealthy after
    /// startup is liveness's concern, not readiness's.
    /// </summary>
    public void MarkReady() => _ready = true;
}

/// <summary>
/// Health check that reports the <see cref="StartupGate"/>'s state. Registered
/// under the <c>ready</c> tag and surfaced at <c>/ready</c>.
/// </summary>
public sealed class StartupGateHealthCheck(StartupGate gate) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(gate.IsReady
            ? HealthCheckResult.Healthy("Startup complete.")
            : HealthCheckResult.Unhealthy("Startup work has not finished."));
}
