using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Auth;
using SprintSync.Api.Data;

namespace SprintSync.Api.Tenancy;

/// <summary>
/// Resolves the server-verified ambient organization for the request
/// (Principle VI, research R1). The requested org is taken from the
/// <c>X-Organization-Id</c> header when present, else the user's persisted
/// active org. It is used ONLY if a <c>Membership</c> confirms it. A
/// client-supplied id that fails verification yields no tenant (org-scoped
/// access will 404). A stale persisted active org falls back to another
/// membership or the empty state, self-healing the stored value (FR-014).
///
/// This is the single sanctioned cross-org read path (Principle V carve-out) —
/// it queries the unfiltered Membership table directly.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string OrganizationHeader = "X-Organization-Id";

    public async Task InvokeAsync(
        HttpContext context, AppDbContext db, ICurrentUser currentUser, ITenantContext tenant)
    {
        if (currentUser.User is { } user)
        {
            Guid? resolved = null;

            if (TryGetHeaderOrg(context, out var headerOrg))
            {
                // Explicit client-supplied org: honored only if verified.
                if (await IsMemberAsync(db, user.Id, headerOrg))
                {
                    resolved = headerOrg;
                }
            }
            else if (user.ActiveOrganizationId is { } activeOrg)
            {
                if (await IsMemberAsync(db, user.Id, activeOrg))
                {
                    resolved = activeOrg;
                }
                else
                {
                    // Stale/invalid persisted selection — fall back and self-heal.
                    resolved = await db.Memberships
                        .Where(m => m.UserId == user.Id)
                        .Select(m => (Guid?)m.OrganizationId)
                        .FirstOrDefaultAsync();
                    user.ActiveOrganizationId = resolved;
                    await db.SaveChangesAsync();
                }
            }

            tenant.SetOrganization(resolved);
        }

        await next(context);
    }

    private static bool TryGetHeaderOrg(HttpContext context, out Guid organizationId)
    {
        organizationId = default;
        var value = context.Request.Headers[OrganizationHeader].ToString();
        return !string.IsNullOrEmpty(value) && Guid.TryParse(value, out organizationId);
    }

    private static Task<bool> IsMemberAsync(AppDbContext db, Guid userId, Guid organizationId) =>
        db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId);
}
