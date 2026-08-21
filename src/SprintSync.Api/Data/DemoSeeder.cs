using Microsoft.EntityFrameworkCore;
using SprintSync.Api.Data.Entities;

namespace SprintSync.Api.Data;

/// <summary>
/// Seeds a fixed cast of users, organizations and memberships (T047).
///
/// Deterministic on purpose: every id is a literal, so a demo can be scripted,
/// a bug report can name a row, and re-running the app changes nothing. The
/// shape is chosen to make the isolation story visible at a glance — two users
/// with an organization each, one shared organization between them, and a third
/// user who belongs to nothing so the empty state is always reachable.
///
/// Seeding is opt-in via <c>DemoData:Enabled</c>, which the Aspire AppHost turns
/// ON for the deployed demo too — this is a demo app, so a populated cast is the
/// intended deployed state (P2-12). It is idempotent and never runs against a
/// database that already has organizations, so it cannot clobber real data. The
/// integration suite leaves it off, so tests never inherit rows they did not
/// create.
/// </summary>
public static class DemoSeeder
{
    // Stable identities. The external ids match what a local test token carries.
    public static readonly Guid AliceId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid BobId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CarolId = new("33333333-3333-3333-3333-333333333333");

    public static readonly Guid AcmeId = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid GammaId = new("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid SharedId = new("aaaaaaaa-0000-0000-0000-000000000003");

    public const string AliceExternalId = "demo-alice";
    public const string BobExternalId = "demo-bob";
    public const string CarolExternalId = "demo-carol";

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        // Idempotent: if anything is already here, leave it alone rather than
        // half-merging into someone's working data.
        if (await db.Organizations.AnyAsync(cancellationToken))
        {
            return;
        }

        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var alice = new User
        {
            Id = AliceId,
            ExternalId = AliceExternalId,
            DisplayName = "Alice (Acme + Shared)",
            ActiveOrganizationId = AcmeId,
            CreatedAt = timestamp,
        };
        var bob = new User
        {
            Id = BobId,
            ExternalId = BobExternalId,
            DisplayName = "Bob (Gamma + Shared)",
            ActiveOrganizationId = GammaId,
            CreatedAt = timestamp,
        };

        // Carol exists but belongs to nothing — the empty state, always available
        // to demo without having to delete anything first.
        var carol = new User
        {
            Id = CarolId,
            ExternalId = CarolExternalId,
            DisplayName = "Carol (no organizations)",
            ActiveOrganizationId = null,
            CreatedAt = timestamp,
        };

        db.Users.AddRange(alice, bob, carol);

        db.Organizations.AddRange(
            new Organization { Id = AcmeId, Name = "Acme", CreatedByUserId = AliceId, CreatedAt = timestamp },
            new Organization { Id = GammaId, Name = "Gamma", CreatedByUserId = BobId, CreatedAt = timestamp },
            new Organization { Id = SharedId, Name = "Shared Ventures", CreatedByUserId = AliceId, CreatedAt = timestamp });

        // Alice: Acme + Shared. Bob: Gamma + Shared. Neither can see the other's
        // private org — which is the whole point of the demo data.
        db.Memberships.AddRange(
            Membership(AliceId, AcmeId, timestamp),
            Membership(AliceId, SharedId, timestamp),
            Membership(BobId, GammaId, timestamp),
            Membership(BobId, SharedId, timestamp));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Membership Membership(Guid userId, Guid organizationId, DateTimeOffset timestamp) =>
        new()
        {
            // Derived from the pair so re-seeding produces identical rows.
            Id = Deterministic(userId, organizationId),
            UserId = userId,
            OrganizationId = organizationId,
            Role = OrgRole.Owner,
            CreatedAt = timestamp,
        };

    private static Guid Deterministic(Guid userId, Guid organizationId)
    {
        Span<byte> bytes = stackalloc byte[16];
        Span<byte> user = stackalloc byte[16];
        Span<byte> org = stackalloc byte[16];
        userId.TryWriteBytes(user);
        organizationId.TryWriteBytes(org);

        for (var i = 0; i < 16; i++)
        {
            bytes[i] = (byte)(user[i] ^ org[i]);
        }

        return new Guid(bytes);
    }
}
