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
//
// KNOWN ISSUE (Aspire 13.4.6): this call registers a second deployment target
// for the API on top of the one the SDK infers, so `azd infra synth` fails with
// "Sequence contains more than one matching element" before it can emit the
// container-app Bicep. The SQL module still generates and was verified. Confirm
// `minReplicas: 0` in the synthesized output before the first deploy — see the
// pre-deploy guard in quickstart.md (T045).
builder.AddAzureContainerAppEnvironment("cae");

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

var api = builder.AddProject<Projects.SprintSync_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints()
    // Deterministic demo cast for local runs only (T047).
    .WithEnvironment("DemoData__Enabled", "true");

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
