using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Auth;

/// <summary>
/// Scoped accessor for the JIT-resolved application <see cref="User"/> of the
/// current request. Populated by <see cref="UserProvisioningMiddleware"/>.
/// </summary>
public interface ICurrentUser
{
    User? User { get; }
    bool IsResolved => User is not null;
    Guid Id => User?.Id ?? throw new InvalidOperationException("No current user resolved.");
    void Set(User user);
}

public sealed class CurrentUser : ICurrentUser
{
    public User? User { get; private set; }
    public void Set(User user) => User = user;
}
