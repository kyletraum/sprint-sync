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
    options.UseSqlServer(builder.Configuration.GetConnectionString("sprintsync")));

builder.Services.AddSprintSyncAuth(builder.Configuration);

var app = builder.Build();

// Uniform error contract (Principle III): unhandled exceptions and otherwise
// bodyless status codes are rendered as RFC 9457 ProblemDetails, never a bare
// framework 500. Registered first so it wraps the whole pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();

// Published OpenAPI (Principle III) — always, not dev-only.
app.MapOpenApi();

// Pipeline order matters: authenticate, resolve the app user (JIT), resolve and
// verify the ambient tenant, THEN authorize (so OrgMember sees the tenant).
app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// Versioned contract (Principle II). New versions get a new prefix.
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
// tests), and in Production only when explicitly opted in — safe here because
// the API publishes with MaxReplicas = 1, so there is no concurrent-migration
// race. The Aspire AppHost sets Database:MigrateOnStartup for the deployed API
// so the schema exists on first request (P0-2).
var migrateOnStartup = !app.Environment.IsProduction()
    || app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
if (migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Demo data is opt-in rather than "on in Development", so the integration
    // suite — which also runs in Development — never inherits rows it did not
    // create (T047).
    if (app.Configuration.GetValue<bool>("DemoData:Enabled"))
    {
        await DemoSeeder.SeedAsync(db);
    }
}

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program;
