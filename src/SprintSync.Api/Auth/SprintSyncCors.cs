namespace SprintSync.Api.Auth;

/// <summary>
/// The single CORS policy name, so registration and use cannot drift apart — a
/// mismatched name silently yields a policy that permits nothing, which is
/// indistinguishable from CORS being misconfigured.
/// </summary>
public static class SprintSyncCors
{
    public const string PolicyName = "sprint-sync-spa";
}
