using SprintSync.Api.Auth;
using SprintSync.Api.Contracts;

namespace SprintSync.Api.Features.Me;

public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group)
    {
        // GET /me — current user + active org. JIT provisioning and stale-active
        // -org fallback have already been applied by the middleware pipeline
        // (FR-013, FR-014).
        group.MapGet("/me", (ICurrentUser currentUser) =>
        {
            if (currentUser.User is not { } user)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new MeResponse(user.Id, user.DisplayName, user.ActiveOrganizationId));
        })
        .WithTags("Me")
        .RequireAuthorization()
        .WithName("GetMe");

        return group;
    }
}
