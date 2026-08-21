using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Auth;
using SprintSync.Api.Contracts;
using SprintSync.Api.Data;
using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Features.Organizations;

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder group)
    {
        // GET /organizations — the caller's organizations (FR-005, SC-003).
        // The sanctioned cross-organization read (Principle V carve-out): scoped
        // by verified Membership, NOT by the ambient tenant, so it deliberately
        // does not require the OrgMember policy — a user with no memberships
        // gets an empty page rather than a denial.
        group.MapGet("/organizations", async (
            ICurrentUser currentUser, AppDbContext db, int? page, int? pageSize) =>
        {
            if (currentUser.User is not { } user)
            {
                return Results.Unauthorized();
            }

            var paging = PageRequest.From(page, pageSize);

            var memberships = db.Memberships.Where(m => m.UserId == user.Id);
            var totalCount = await memberships.CountAsync();

            // Stable ordering so pages partition the set without overlap or gaps.
            var items = await memberships
                .OrderBy(m => m.Organization.Name)
                .ThenBy(m => m.OrganizationId)
                .Skip(paging.Skip)
                .Take(paging.PageSize)
                .Select(m => new OrganizationSummary(m.OrganizationId, m.Organization.Name, m.Role))
                .ToListAsync();

            return Results.Ok(new PagedResult<OrganizationSummary>(
                items, paging.Page, paging.PageSize, totalCount));
        })
        .WithTags("Organizations")
        .RequireAuthorization()
        .Produces<PagedResult<OrganizationSummary>>(200)
        .WithName("ListMyOrganizations");

        // GET /organizations/{organizationId} — hide-existence read (FR-008/009).
        //
        // One membership-gated query answers both "does it exist?" and "may you
        // see it?". There is deliberately no prior existence lookup and no
        // branch between the two failure modes: a non-member and an unknown id
        // travel the identical path and produce the identical 404, so neither
        // the response nor its latency reveals which case occurred (research R4).
        group.MapGet("/organizations/{organizationId:guid}", async (
            Guid organizationId, ICurrentUser currentUser, AppDbContext db) =>
        {
            if (currentUser.User is not { } user)
            {
                return Results.Unauthorized();
            }

            var detail = await db.Organizations
                .Where(o => o.Id == organizationId
                    && o.Memberships.Any(m => m.UserId == user.Id))
                .Select(o => new OrganizationDetail(
                    o.Id,
                    o.Name,
                    o.Memberships.First(m => m.UserId == user.Id).Role,
                    o.Memberships.Count,
                    o.CreatedAt))
                .FirstOrDefaultAsync();

            return detail is null ? HideExistence.NotFound() : Results.Ok(detail);
        })
        .WithTags("Organizations")
        .RequireAuthorization()
        .Produces<OrganizationDetail>(200)
        .Produces(404)
        .WithName("GetOrganization");

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
        .Produces<OrganizationSummary>(201)
        .ProducesValidationProblem()
        .WithName("CreateOrganization");

        return group;
    }
}
