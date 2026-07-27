using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Data;
using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Auth;

/// <summary>
/// Just-in-time provisioning (FR-013): on the first authenticated request from
/// an identity the app has not seen, creates a local <see cref="User"/> (with
/// zero memberships) and attaches it to <see cref="ICurrentUser"/>. Account
/// creation itself is owned by the external IdP.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, ICurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var externalId = context.User.GetExternalId();
            if (!string.IsNullOrEmpty(externalId))
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);
                if (user is null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = externalId,
                        DisplayName = context.User.GetDisplayName(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                }

                currentUser.Set(user);
            }
        }

        await next(context);
    }
}
