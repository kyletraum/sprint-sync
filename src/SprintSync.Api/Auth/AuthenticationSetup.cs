using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;

namespace SprintSync.Api.Auth;

public static class AuthenticationSetup
{
    /// <summary>
    /// Authentication is Entra External ID only (Principle VII). Registers JWT
    /// bearer validation, the current-user accessor, the OrgMember policy, and
    /// the hide-existence 404 result handler.
    /// </summary>
    public static IServiceCollection AddSprintSyncAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var azureAd = configuration.GetSection("AzureAd");

        // Fail fast, loudly, at startup rather than 401-ing every request in the
        // cloud: outside Development the Entra config MUST be real, not the
        // appsettings placeholders. The AppHost passes these from azd env values;
        // see the deploy section of quickstart.md (P0-1).
        if (!environment.IsDevelopment())
        {
            foreach (var key in new[] { "Instance", "TenantId", "ClientId", "Audience" })
            {
                var value = azureAd[key];
                if (string.IsNullOrWhiteSpace(value)
                    || value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"AzureAd:{key} is not configured. Set all of AzureAd__Instance/TenantId/"
                        + "ClientId/Audience (e.g. `azd env set`) before running outside Development "
                        + "— otherwise authentication binds to placeholders and requests fail (P1-5).");
                }
            }
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(azureAd);

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IAuthorizationHandler, OrgMemberHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, HideExistenceAuthorizationResultHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.OrgMember, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new OrgMemberRequirement()));

        return services;
    }
}
