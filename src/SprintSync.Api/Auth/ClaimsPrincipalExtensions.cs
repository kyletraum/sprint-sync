using System.Security.Claims;

namespace SprintSync.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// The stable external identity used as <c>User.ExternalId</c> — the Entra
    /// <c>oid</c> object identifier, falling back to <c>sub</c>/NameIdentifier.
    /// </summary>
    public static string? GetExternalId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("oid")
        ?? principal.FindFirstValue(ObjectIdClaim)
        ?? principal.FindFirstValue("sub")
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name);
}
