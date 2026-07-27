using Microsoft.AspNetCore.Authorization;
using SprintSync.Api.Tenancy;

namespace SprintSync.Api.Auth;

/// <summary>
/// Authorization for endpoints that operate within the ambient organization:
/// the request must have a server-resolved, membership-verified tenant
/// (Principle VIII). Denials on these resources are mapped to 404 by
/// <see cref="HideExistenceAuthorizationResultHandler"/> so authorization never
/// confirms existence.
/// </summary>
public sealed class OrgMemberRequirement : IAuthorizationRequirement;

public sealed class OrgMemberHandler : AuthorizationHandler<OrgMemberRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OrgMemberRequirement requirement)
    {
        // Resource is the HttpContext under endpoint routing; resolve the scoped
        // tenant from request services.
        if (context.Resource is HttpContext http)
        {
            var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
            if (tenant.HasTenant)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

public static class AuthorizationPolicies
{
    public const string OrgMember = nameof(OrgMember);
}
