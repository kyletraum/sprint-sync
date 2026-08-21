using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Auth;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data;

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
        .Produces<MeResponse>(200)
        .WithName("GetMe");

        // PUT /me/active-organization — switch the acting organization
        // (FR-006/007/010). The supplied id is a request, not an authority: it
        // is written only after the server confirms a Membership. A target the
        // caller does not belong to gets the same uniform 404 as one that does
        // not exist, and the previous selection is left untouched.
        group.MapPut("/me/active-organization", async (
            SetActiveOrganizationRequest request, ICurrentUser currentUser, AppDbContext db) =>
        {
            if (currentUser.User is not { } user)
            {
                return Results.Unauthorized();
            }

            // A missing/empty id is a malformed client request (400), distinct
            // from a well-formed id the caller isn't a member of (404, P2-7).
            if (request.OrganizationId == Guid.Empty)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["organizationId"] = ["A non-empty organizationId is required."],
                });
            }

            var isMember = await db.Memberships.AnyAsync(m =>
                m.UserId == user.Id && m.OrganizationId == request.OrganizationId);

            if (!isMember)
            {
                return HideExistence.NotFound();
            }

            user.ActiveOrganizationId = request.OrganizationId;
            await db.SaveChangesAsync();

            // Echo the updated state so the caller needs no follow-up GET /me.
            return Results.Ok(new MeResponse(user.Id, user.DisplayName, user.ActiveOrganizationId));
        })
        .WithTags("Me")
        .RequireAuthorization()
        .Produces<MeResponse>(200)
        .Produces(404)
        .WithName("SetActiveOrganization");

        return group;
    }
}
