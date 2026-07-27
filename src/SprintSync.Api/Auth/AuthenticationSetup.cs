using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IAuthorizationHandler, OrgMemberHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, HideExistenceAuthorizationResultHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.OrgMember, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new OrgMemberRequirement()));

        return services;
    }
}
