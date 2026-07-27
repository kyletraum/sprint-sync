using SprintSync.Api.Auth;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data;
using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Features.Organizations;

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder group)
    {
        // POST /organizations — create an org, become its Owner (FR-001/002/003).
        group.MapPost("/organizations", async (
            CreateOrganizationRequest request, ICurrentUser currentUser, AppDbContext db) =>
        {
            if (currentUser.User is not { } user)
            {
                return Results.Unauthorized();
            }

            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required and must be 1-100 characters."],
                });
            }

            var now = DateTimeOffset.UtcNow;
            var org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = name,
                CreatedByUserId = user.Id,
                CreatedAt = now,
            };
            db.Organizations.Add(org);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OrganizationId = org.Id,
                Role = OrgRole.Owner,
                CreatedAt = now,
            });

            // First org becomes the active org (FR-014).
            if (user.ActiveOrganizationId is null)
            {
                user.ActiveOrganizationId = org.Id;
            }

            await db.SaveChangesAsync();

            var summary = new OrganizationSummary(org.Id, org.Name, OrgRole.Owner);
            return Results.Created($"/api/v1/organizations/{org.Id}", summary);
        })
        .WithTags("Organizations")
        .RequireAuthorization()
        .WithName("CreateOrganization");

        return group;
    }
}
