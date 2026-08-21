using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        HttpContext context, AppDbContext db, ICurrentUser currentUser, ITenantContext tenant,
        ILogger<TenantResolutionMiddleware> logger)
    {
        if (currentUser.User is { } user)
        {
            Guid? resolved = null;

            // A header is an "act-as" hint, honored ONLY when the server verifies
            // the membership itself. Absent, malformed, or naming an org the caller
            // is not a member of, it falls through to the persisted active org —
            // consistently, so the header never nukes the caller's own context and
            // never grants a non-member org (P2-9).
            if (TryGetHeaderOrg(context, out var headerOrg)
                && await IsMemberAsync(db, user.Id, headerOrg))
            {
                resolved = headerOrg;
            }
            else if (user.ActiveOrganizationId is { } activeOrg
                && await IsMemberAsync(db, user.Id, activeOrg))
            {
                // A still-valid persisted selection.
                resolved = activeOrg;
            }
            else
            {
                // No usable selection: the persisted active org is stale/invalid,
                // OR it is null while the user does belong to organizations (a
                // future invite/join flow). Fall back to the first membership by a
                // stable (name, id) order and self-heal the stored value —
                // symmetric across both cases (P2-7, P2-13).
                resolved = await db.Memberships
                    .Where(m => m.UserId == user.Id)
                    .OrderBy(m => m.Organization.Name)
                    .ThenBy(m => m.OrganizationId)
                    .Select(m => (Guid?)m.OrganizationId)
                    .FirstOrDefaultAsync();

                // Persisting the repair is a best-effort OPTIMIZATION: the resolved
                // value is applied in-memory below regardless, so a failed write
                // must not 500 an otherwise-successful (idempotent) GET — it re-heals
                // next request (P1-2). Narrowed to DB faults and raised to Warning so
                // a persistent failure is observable, not silent (P2-8).
                //
                // Skipped on act-as (X-Organization-Id) requests and when the value
                // is unchanged: an act-as request must not mutate the persisted active
                // org, and skipping narrows a lost-update race against a concurrent
                // PUT /me/active-organization. Accepted semantics otherwise:
                // last-writer-wins on this single per-user field (P1-5).
                var isActAsRequest = context.Request.Headers.ContainsKey(OrganizationHeader);
                if (!isActAsRequest && user.ActiveOrganizationId != resolved)
                {
                    try
                    {
                        user.ActiveOrganizationId = resolved;
                        await db.SaveChangesAsync();
                    }
                    catch (DbUpdateException ex)
                    {
                        logger.LogWarning(
                            ex, "Active-org self-heal write failed; will retry on the next request.");
                    }
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
