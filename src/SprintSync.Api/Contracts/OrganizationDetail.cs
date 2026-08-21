using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Contracts;

/// <summary>
/// A single organization the caller is a member of. Only ever produced by the
/// membership-gated fetch — a non-member never receives this shape, they
/// receive the uniform hide-existence 404 (FR-008/009).
/// </summary>
public sealed record OrganizationDetail(
    Guid Id, string Name, OrgRole Role, int MemberCount, DateTimeOffset CreatedAt);
