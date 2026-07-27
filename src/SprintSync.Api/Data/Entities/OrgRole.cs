namespace SprintSync.Api.Data.Entities;

/// <summary>
/// Organization-level role. Only <see cref="Owner"/> exists in this slice;
/// Admin and Member are reserved for later features (Principle VIII).
/// </summary>
public enum OrgRole
{
    Owner = 0,
}
