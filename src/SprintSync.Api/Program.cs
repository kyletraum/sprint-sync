using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using SprintSync.Api.Auth;
using SprintSync.Api.Data;
using SprintSync.Api.Features.Me;
using SprintSync.Api.Features.Organizations;
using SprintSync.Api.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Contract conventions (Principle III): ProblemDetails + string enums.
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// OpenAPI document is a published deliverable (Principle III): /openapi/v1.json.
// Its identity is pinned to contracts/openapi.yaml rather than left to the
// assembly name, because the two are checked against each other (T046).
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Sprint Sync API",
            Version = "1.0",
            Description =
                "Public, versioned contract for Sprint Sync. The React web app and any "
                + "third-party client consume exactly these endpoints (Principle I).",
        };
        return Task.CompletedTask;
    });
});

// Persistence: scoped (NOT pooled) so the global query filter reads the correct
// per-request tenant (research R2).
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("sprintsync"), sql =>
    {
        // The deliberate topology — ACA scale-to-zero + Azure SQL serverless
        // auto-pause — guarantees the first request after idle hits a RESUMING
        // database, so transient faults are the expected path, not exceptional.
        // Retry them instead of failing the request (P1-2).
        sql.EnableRetryOnFailure();
        sql.CommandTimeout(60);
    }));

builder.Services.AddSprintSyncAuth(builder.Configuration, builder.Environment);

// Browser access for the hosted SPA (feature 002, FR-030). The web app is served
// from Static Web Apps on its own origin, so every call it makes to this API is
// cross-origin and fails at the browser's preflight check without this — while
// curl, which does not enforce CORS, keeps working. That asymmetry makes the
// failure confusing to diagnose, so it is configured explicitly rather than
// discovered.
//
// NAMED ORIGINS ONLY. This is a credentialed, tenant-scoped API: AllowAnyOrigin
// together with credentials is both a security hole and rejected outright by
// browsers. Origins come from configuration (Cors:AllowedOrigins) so each
// environment supplies its own without a code change; with none configured the
// policy simply permits nothing, which is the correct default for an API whose
// only browser client is a deployment artefact.
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy(SprintSyncCors.PolicyName, policy =>
    {
        if (corsOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    }));

var app = builder.Build();

// Uniform error contract (Principle III): unhandled exceptions and otherwise
// bodyless status codes are rendered as RFC 9457 ProblemDetails, never a bare
// framework 500. Registered first so it wraps the whole pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();

// Published OpenAPI (Principle III) — always, not dev-only.
app.MapOpenApi();

// CORS runs before authentication so that preflight OPTIONS requests — which
// browsers send without credentials — are answered rather than rejected as
// unauthenticated. Placed after the error handlers so a CORS failure still
// renders as ProblemDetails.
app.UseCors(SprintSyncCors.PolicyName);

// Pipeline order matters: authenticate, resolve the app user (JIT), resolve and
// verify the ambient tenant, THEN authorize (so OrgMember sees the tenant).
app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// Versioned contract (Principle II). New versions get a new prefix.
//
// CONVENTION (P2-11): every future endpoint that reads or writes a
// TenantScopedEntity MUST call .RequireAuthorization(AuthorizationPolicies.OrgMember)
// so the ambient tenant is verified before the handler runs. Isolation is
// structural (Principles V/VIII) — never a per-handler membership check.
var v1 = app.MapGroup("/api/v1");
v1.MapMeEndpoints();
v1.MapOrganizationEndpoints();

// Test-only endpoints — off unless TestEndpoints:Enabled, never mapped in
// production. They exercise pipeline paths no production endpoint reaches yet:
// the OrgMember policy's 403->404 hide-existence mapping (P1-9) and the
// ProblemDetails rendering of an unhandled exception (P1-3).
if (app.Configuration.GetValue<bool>("TestEndpoints:Enabled"))
{
    v1.MapGet("/_test/org-scoped", () => Results.Ok(new { ok = true }))
        .RequireAuthorization(SprintSync.Api.Auth.AuthorizationPolicies.OrgMember)
        .ExcludeFromDescription()
        .WithName("TestOrgScoped");

    v1.MapGet("/_test/boom", IResult () => throw new InvalidOperationException("boom"))
        .ExcludeFromDescription()
        .WithName("TestBoom");
}

// Schema is applied on startup outside Production (local dev + integration
// tests), and in Production only when Database:MigrateOnStartup is set — the
// Aspire AppHost sets it for the deployed API so the schema exists on first
// request (P0-2).
//
// CONCURRENCY (T050): MaxReplicas = 1 rules out a race WITHIN one revision, and
// StartupMigrator takes a session-scoped SQL application lock around MigrateAsync
// to rule out the cross-revision one — a rolling update (Single revision mode)
// can otherwise start the new revision's migration while the old revision is
// still live. A second process now waits for the first instead of applying DDL
// alongside it. Transient faults remain retried by the execution strategy.
var migrateOnStartup = !app.Environment.IsProduction()
    || app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
if (migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await StartupMigrator.MigrateAsync(db, app.Logger);

    // Demo data is opt-in rather than "on in Development", so the integration
    // suite — which also runs in Development — never inherits rows it did not
    // create (T047).
    if (app.Configuration.GetValue<bool>("DemoData:Enabled"))
    {
        await DemoSeeder.SeedAsync(db);
    }
}

// Startup work is done — the service can now serve requests, so readiness flips
// and the platform may route traffic to this instance (Principle X, T033).
//
// This MUST stay after the migration/seed block: the whole point of readiness is
// that the process is listening but not yet servable during that window. Moving
// it earlier would make /ready return 200 while the schema is still being
// applied, which is exactly the lie readiness exists to prevent.
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program;
