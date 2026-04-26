namespace PiggyBank.Core.Entities;

/// <summary>
/// Records that some portion of a <see cref="SideIncomeEntry"/> has been
/// moved into a <see cref="Pocket"/> or converted into an income
/// <see cref="Transaction"/> on the main ledger. Multiple allocations
/// per entry are allowed — the UI surfaces remaining = Total − Σ allocations
/// so users can split across targets.
/// </summary>
public sealed class SideIncomeAllocation : ProfileOwnedEntity
{
    public required Guid SideIncomeEntryId { get; set; }

    /// <summary>Always positive. Never exceeds the parent entry's Total.</summary>
    public required decimal Amount { get; set; }

    public required SideIncomeAllocationTarget Target { get; set; }

    /// <summary>Set when <see cref="Target"/> is <see cref="SideIncomeAllocationTarget.Pocket"/>.
    /// The corresponding <see cref="Deposit"/> (and single <see cref="DepositAllocation"/>)
    /// is referenced by <see cref="PocketDepositId"/>.</summary>
    public Guid? PocketId { get; set; }

    /// <summary>The Deposit row created for this allocation when the target
    /// was a pocket. Lets the pocket's history show the side-income source.</summary>
    public Guid? PocketDepositId { get; set; }

    /// <summary>Set when <see cref="Target"/> is <see cref="SideIncomeAllocationTarget.MainLedger"/>.
    /// The ledger month the income transaction landed in.</summary>
    public Guid? MonthId { get; set; }

    /// <summary>The income Transaction created for this allocation when
    /// the target was the main ledger.</summary>
    public Guid? LedgerTransactionId { get; set; }

    public DateTime AllocatedAtUtc { get; set; }

    public string? Notes { get; set; }
}

public enum SideIncomeAllocationTarget
{
    Pocket = 0,
    MainLedger = 1,
}
