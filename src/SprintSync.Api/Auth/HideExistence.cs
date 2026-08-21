using Microsoft.AspNetCore.Mvc;

namespace SprintSync.Api.Auth;

/// <summary>
/// The one "not found" response every hide-existence path returns (FR-009,
/// research R4). Read paths, act paths and authorization denials all emit this
/// exact body, so a non-member response is byte-identical to the response for
/// an organization that never existed — there is no status, message or shape
/// an attacker can use to tell membership from existence.
///
/// The payload is deliberately static: no id, no instance path, no trace
/// identifier. Anything that varied per request would be a channel.
/// </summary>
public static class HideExistence
{
    private static readonly ProblemDetails NotFoundProblem = new()
    {
        Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
        Title = "Not Found",
        Status = StatusCodes.Status404NotFound,
        Detail = "The requested organization does not exist, or you do not have access to it.",
    };

    public const string ContentType = "application/problem+json";

    /// <summary>The uniform 404 for minimal-API handlers.</summary>
    public static IResult NotFound() =>
        Results.Json(NotFoundProblem, contentType: ContentType, statusCode: StatusCodes.Status404NotFound);

    /// <summary>The same response written directly, for the authorization layer.</summary>
    public static Task WriteAsync(HttpContext context) => NotFound().ExecuteAsync(context);
}
