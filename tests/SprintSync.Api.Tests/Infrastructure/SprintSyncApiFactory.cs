using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SprintSync.Api.Data;

namespace SprintSync.Api.Tests.Infrastructure;

/// <summary>
/// Boots the API against the SQL test container and replaces Entra with the
/// test authentication scheme. Migrations are applied by the app on startup.
/// </summary>
public sealed class SprintSyncApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:sprintsync", connectionString);
        builder.UseSetting("TestEndpoints:Enabled", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Test-only window onto the resolved ambient tenant (FR-010).
            services.AddSingleton<IStartupFilter, TenantProbeStartupFilter>();
        });
    }

    /// <summary>An HTTP client that authenticates as the given external user.</summary>
    public HttpClient CreateClientFor(string externalUserId, string? name = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, externalUserId);
        if (name is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, name);
        }

        return client;
    }

    /// <summary>
    /// Direct database access for seeding states the API has no endpoint for
    /// yet (adding a second member, revoking a membership).
    /// </summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>
/// Boots the API in <b>Production</b>, which is the only environment where the
/// probe contract can actually be verified: <c>/health</c> is Development-only,
/// so a readiness probe pointed at it 404s in Azure, every probe fails, and the
/// revision never goes Ready. A Development-hosted test cannot catch that —
/// there, <c>/health</c> exists and everything looks fine.
///
/// Entra is replaced with the test scheme as usual; the API's Production
/// fail-fast guard would otherwise refuse to start without real AzureAd values.
/// </summary>
/// <param name="configureTestServices">
/// Optional extra test-only registrations, applied after the standard ones.
/// Used by ProbeTracingTests to install an in-memory span exporter, which is the
/// only way to observe what the OpenTelemetry pipeline actually exports.
/// </param>
public sealed class ProductionApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureTestServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:sprintsync", connectionString);

        // Production requires these; the values are never used because the test
        // authentication scheme below replaces the Entra handler outright.
        builder.UseSetting("AzureAd:Instance", "https://test.ciamlogin.com/");
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:Audience", "api://test");

        // Apply the schema so startup completes and readiness can flip.
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("DemoData:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            configureTestServices?.Invoke(services);
        });
    }
}

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
