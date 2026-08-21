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

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
