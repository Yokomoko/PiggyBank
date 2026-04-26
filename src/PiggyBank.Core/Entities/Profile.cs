namespace PiggyBank.Core.Entities;

/// <summary>
/// A tenant. Does not inherit <see cref="ProfileOwnedEntity"/> — profiles
/// themselves are the tenancy axis. Only <c>ProfileAdminService</c>
/// reads/writes this table, always via <c>IgnoreQueryFilters</c>.
/// </summary>
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public string ColourHex { get; set; } = "#3B82F6";
    public string IconKey { get; set; } = "person";
    public byte[]? PinHash { get; set; }   // DPAPI-wrapped — Phase 3
    public byte[]? PinSalt { get; set; }
    /// <summary>Set by <c>ProfileAdminService.CreateAsync</c> using the injected
    /// <see cref="TimeProvider"/>; never defaulted to <c>DateTime.UtcNow</c>
    /// at the entity level (would violate the portable-core determinism rule).</summary>
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}
