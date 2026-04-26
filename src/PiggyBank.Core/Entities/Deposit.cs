namespace PiggyBank.Core.Entities;

/// <summary>
/// A single top-up into the savings system (e.g. "I moved £500 into my
/// auto-saver on the 24th"). The <see cref="DepositService"/> creates
/// one <see cref="DepositAllocation"/> per pocket that received a share,
/// frozen at the auto-save percentages as they were the moment the
/// deposit was recorded.
/// </summary>
public sealed class Deposit : ProfileOwnedEntity
{
    public required DateOnly DepositedOn { get; set; }
    public required decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
