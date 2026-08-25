using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string ReadinessEndpointPath = "/ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing.
                        //
                        // All THREE probe paths, not just the two the Aspire
                        // template ships. /ready was added in this feature and was
                        // initially missed here (S6, committee round 1-2) — and it
                        // is the one that matters most: the container app polls
                        // readiness at periodSeconds: 5 against liveness at 30, so
                        // it is SIX TIMES the volume of the probe that was already
                        // excluded. That is ~720 spans/hour per warm replica, all
                        // of them shipped by UseAzureMonitor(), which would have
                        // dominated App Insights transaction search and buried real
                        // request traces — the opposite of what Principle X wants
                        // telemetry for. It also bills.
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                            && !context.Request.Path.StartsWithSegments(ReadinessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Azure Monitor export (Principle X). Gated on the connection string the
        // AppHost supplies to the deployed container app: with no destination
        // configured — local `dotnet run`, integration tests, CI — the service
        // starts and serves exactly as before (FR-019). Telemetry that cannot be
        // exported must never become an availability problem (FR-020).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Readiness is a distinct signal from liveness (Principle X): liveness
        // failure means restart, readiness failure means withhold traffic. The
        // gate is a singleton so the endpoint and the startup path share state.
        builder.Services.AddSingleton<StartupGate>();

        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            // Readiness: not ready until startup work (migration, seeding) is done.
            // Deliberately performs NO database access — see StartupGate.
            .AddCheck<StartupGateHealthCheck>("startup", tags: ["ready"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness (tagged "live", returns no detail) is exposed in every
        // environment so Azure Container Apps probes work in Production (P2-17).
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        // Readiness, exposed in EVERY environment because the ACA readiness probe
        // runs in Production (Principle X). It returns status only — no per-check
        // detail, no exception text, no dependency names — which is exactly what
        // makes it safe here while /health below is not.
        //
        // Do NOT point a probe at /health: it is Development-only, so in Azure it
        // 404s, every probe fails, the revision never goes Ready, and the
        // deployment hangs or rolls back. See specs/002-azure-deploy-baseline/
        // research.md R2 and contracts/health-endpoints.md.
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        });

        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for the app to be considered ready.
            // Development-only for the security reason noted here:
            // https://aka.ms/aspire/healthchecks — it returns per-check detail.
            app.MapHealthChecks(HealthEndpointPath);
        }

        return app;
    }
}
