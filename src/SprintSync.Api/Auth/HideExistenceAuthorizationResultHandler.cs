using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace SprintSync.Api.Auth;

/// <summary>
/// Maps <see cref="OrgMemberRequirement"/> denials to <c>404 Not Found</c>
/// instead of the default <c>403 Forbidden</c> (T034, research R4). A 403 would
/// confirm that the organization exists; returning 404 keeps a non-member
/// response indistinguishable from a nonexistent organization (FR-009) while
/// authorization stays in the one policy layer (Principle VIII).
/// </summary>
public sealed class HideExistenceAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure is { } failure
            && failure.FailedRequirements.OfType<OrgMemberRequirement>().Any())
        {
            await Results.NotFound().ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
