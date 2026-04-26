namespace PiggyBank.Core.Entities;

/// <summary>
/// System-wide singleton (Id = 1). Holds settings that are shared across
/// all profiles on this install.
/// </summary>
public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public string Theme { get; set; } = "System";      // System / Light / Dark
    public Guid? LastProfileId { get; set; }
    public DateTime? LastProfileOpenedAtUtc { get; set; }
    public string InstallVersion { get; set; } = "0.0.0";
}
