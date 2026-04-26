namespace PiggyBank.Core.Entities;

/// <summary>
/// One pocket's share of a <see cref="Deposit"/>. Snapshots the
/// <see cref="Pocket.AutoSavePercent"/> at deposit time so later autosave
/// edits don't retroactively change history, mirroring the
/// <see cref="MonthlyOutgoing"/> snapshot pattern already used for
/// recurring outgoings.
/// </summary>
public sealed class DepositAllocation : ProfileOwnedEntity
{
    public required Guid DepositId { get; set; }
    public required Guid PocketId { get; set; }

    /// <summary>Whole-percent (0..100) in force at deposit time.</summary>
    public required decimal AutoSavePercentAtDeposit { get; set; }

    /// <summary>£ that flowed into the pocket.</summary>
    public required decimal Amount { get; set; }
}
