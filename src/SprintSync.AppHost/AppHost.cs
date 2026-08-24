using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Sql;

// Aspire AppHost — the single source of truth for service topology.
//
// (Constitution reference corrected 2026-08-22: topology-as-single-source is a
// Technology & Platform Constraint, not a numbered principle. This comment used
// to cite "Principle X", which since constitution v1.1.0 means something quite
// different — Operable by Default, the probes and telemetry configured below.)
//
// Exactly three resources: the SQL database, the API, and the React app. There is
// deliberately no Redis, broker, or other always-on dependency — the active
// organization lives in SQL, not a cache, precisely so nothing has to idle
// billably (Deployment & Cost Constraints, research R8).

var builder = DistributedApplication.CreateBuilder(args);

// The Container Apps environment the API publishes into. Left on the default
// Consumption workload profile deliberately — a dedicated profile bills whether
// or not anything is running, which is exactly what Principle 12 forbids.
// `azd infra gen` emits this with the API container app at minReplicas: 0
// (verified by the cost guard). The earlier synth failure was NOT here — it was
// azure.yaml pairing this Aspire service with a sibling `web` service, which azd
// forbids; fixed there (P1-4).
// Publish-only: the ACA environment is a deploy concept, so skip it in run mode
// to keep local `dotnet run` clean and synth deterministic (P2-11).
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureContainerAppEnvironment("cae");
}

// Telemetry destination (Principle X). Workspace-based Application Insights has
// no standing resource fee — it bills on data ingested into the Log Analytics
// workspace the container app environment already creates, and at idle the app
// scales to zero and emits nothing. So this adds observability without adding a
// standing cost, which the Deployment & Cost Constraints forbid.
//
// Publish-only, like the ACA environment above: local runs get the Aspire
// dashboard, and with no connection string configured the exporter stays off
// entirely (ServiceDefaults gates on it), so nothing changes for `dotnet run`.
IResourceBuilder<Aspire.Hosting.Azure.AzureApplicationInsightsResource>? insights = null;
if (builder.ExecutionContext.IsPublishMode)
{
    insights = builder.AddAzureApplicationInsights("insights");
}

// Azure SQL when published; a plain container locally, so developers need no
// cloud resource to run the stack.
var sql = builder.AddAzureSqlServer("sql")
    .RunAsContainer();

sql.ConfigureInfrastructure(infrastructure =>
{
    // The free serverless offer with auto-pause: the database costs nothing
    // while nobody is using it, at the price of a cold start we have explicitly
    // accepted (Principle 12, research R8).
    foreach (var database in infrastructure.GetProvisionableResources().OfType<SqlDatabase>())
    {
        database.Sku = new SqlSku
        {
            Name = "GP_S_Gen5_1",
            Tier = "GeneralPurpose",
            Family = "Gen5",
            Capacity = 1,
        };
        database.UseFreeLimit = true;
        database.FreeLimitExhaustionBehavior = FreeLimitExhaustionBehavior.AutoPause;

        // Pause after the shortest interval the platform allows.
        database.AutoPauseDelay = 60;
        database.MinCapacity = 0.5;
    }
});

var db = sql.AddDatabase("sprintsync");

// Entra External ID config as azd-sourced parameters (P0-1). At publish these
// become real Bicep parameters wired to the container app's env and resolved
// from azd env at provision time — NOT baked as empty literals. Locally they
// default to empty (Development skips the auth fail-fast guard). For deploy:
//   azd env set AZUREADINSTANCE / AZUREADTENANTID / AZUREADCLIENTID / AZUREADAUDIENCE
var azureAdInstance = builder.AddParameter("AzureAdInstance");
var azureAdTenantId = builder.AddParameter("AzureAdTenantId");
var azureAdClientId = builder.AddParameter("AzureAdClientId");
var azureAdAudience = builder.AddParameter("AzureAdAudience");

var api = builder.AddProject<Projects.SprintSync_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints()
    // Create the schema on the deployed (Production) API too (P0-2). MaxReplicas=1
    // avoids a race within a revision; see the cross-revision caveat in Program.cs (P1-4).
    .WithEnvironment("Database__MigrateOnStartup", "true")
    // Deterministic demo cast.
    .WithEnvironment("DemoData__Enabled", "true")
    // Entra External ID config from parameters, so the deployed container app
    // receives real azd-sourced values — never empty baked literals (P0-1). The
    // API fail-fast guard refuses to start if these are missing in Production.
    .WithEnvironment("AzureAd__Instance", azureAdInstance)
    .WithEnvironment("AzureAd__TenantId", azureAdTenantId)
    .WithEnvironment("AzureAd__ClientId", azureAdClientId)
    .WithEnvironment("AzureAd__Audience", azureAdAudience);

// Supplies APPLICATIONINSIGHTS_CONNECTION_STRING to the deployed container app,
// which is what switches the Azure Monitor exporter on in ServiceDefaults. Only
// in publish mode — locally there is no resource and no connection string, and
// the exporter stays off (Principle X, T036).
if (insights is not null)
{
    api.WithReference(insights);
}

api.PublishAsAzureContainerApp((_, app) =>
{
    // Scale to zero: no requests, no compute bill. The cold start this implies
    // is acceptable at demo scale (Deployment & Cost Constraints).
    app.Template.Scale.MinReplicas = 0;
    app.Template.Scale.MaxReplicas = 1;

    // Health probes (Principle X — Operable by Default). Wired HERE, on the
    // container template, rather than via ConfigureInfrastructure: that is used
    // for the SQL resource above, but the container app is customised through
    // this callback — the same one that already sets scale.
    //
    // Liveness  -> /alive, exposed in every environment.
    // Readiness -> /ready, added in T032, also exposed in every environment.
    //
    // Readiness MUST NOT point at /health: that endpoint is Development-only, so
    // in Azure it 404s, every probe fails, the revision never reports Ready, and
    // the deployment hangs or rolls back. See research.md R2.
    //
    // The readiness check behind /ready performs no database access by design —
    // a per-probe connection would defeat the SQL free-serverless 60s auto-pause
    // and breach the cost constraints silently. See research.md R3.
    // Probe port is taken from the ingress target port rather than written as a
    // literal, so the two cannot drift apart. azd emits that as the generated
    // `api_containerport` parameter; a hardcoded number here would silently stop
    // matching the day the container port changes, and every probe would fail.
    var probePort = app.Configuration.Ingress.TargetPort;

    var container = app.Template.Containers[0].Value!;
    container.Probes.Add(new ContainerAppProbe
    {
        ProbeType = ContainerAppProbeType.Liveness,
        HttpGet = new ContainerAppHttpRequestInfo
        {
            Path = "/alive",
            Port = probePort,
            Scheme = ContainerAppHttpScheme.Http,
        },
        InitialDelaySeconds = 5,
        PeriodSeconds = 30,
        FailureThreshold = 3,
    });
    container.Probes.Add(new ContainerAppProbe
    {
        ProbeType = ContainerAppProbeType.Readiness,
        HttpGet = new ContainerAppHttpRequestInfo
        {
            Path = "/ready",
            Port = probePort,
            Scheme = ContainerAppHttpScheme.Http,
        },
        // Generous failure budget: readiness stays false through schema
        // migration on a cold start, and that must not be mistaken for a fault.
        InitialDelaySeconds = 3,
        PeriodSeconds = 5,
        FailureThreshold = 30,
    });
});

// The React app. Local dev only from the AppHost's point of view — in Azure it
// is deployed to Static Web Apps Free (T044), which keeps the SPA off the ACA
// compute grant and out of the API's lifecycle.
builder.AddViteApp("web", "../../web/sprint-sync-web")
    .WithReference(api)
    .WaitFor(api)
    .WithNpm()
    .ExcludeFromManifest();

builder.Build().Run();
