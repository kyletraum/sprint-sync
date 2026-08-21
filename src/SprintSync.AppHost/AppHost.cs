using Azure.Provisioning.Sql;

// Aspire AppHost — the single source of truth for service topology (Principle X).
//
// Exactly three resources: the SQL database, the API, and the React app. There is
// deliberately no Redis, broker, or other always-on dependency — the active
// organization lives in SQL, not a cache, precisely so nothing has to idle
// billably (Principle 12, research R8).

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

api.PublishAsAzureContainerApp((_, app) =>
{
    // Scale to zero: no requests, no compute bill. The cold start this implies
    // is acceptable at demo scale (Principle 12).
    app.Template.Scale.MinReplicas = 0;
    app.Template.Scale.MaxReplicas = 1;
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
