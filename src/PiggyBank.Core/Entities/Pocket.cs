namespace PiggyBank.Core.Entities;

/// <summary>
/// A named savings pocket. Each pocket has a current balance (manually
/// editable — the user is the source of truth, reconciling to whatever
/// their provider app says), an optional £ goal, an optional auto-save
/// percentage so a <see cref="Deposit"/> can distribute across pockets
/// automatically, and an APR (monthly-accruing).
/// </summary>
/// <remarks>
/// Model intentionally generic — works for any provider that exposes
/// named buckets (auto-saver apps, multi-pot bank accounts, manual
/// spreadsheets). The provider-specific labelling is only a seed-data
/// detail, not a domain rule.
///
/// <c>AutoSavePercent</c> stored as a whole-percent decimal (30 means 30%),
/// not a fraction. Rates stay as decimal fractions (0.0525 = 5.25%) to
/// match <see cref="SavingsProjection"/>'s convention and the calculator.
/// </remarks>
public sealed class Pocket : ProfileOwnedEntity
{
    public required string Name { get; set; }

    /// <summary>Manually-maintained balance. Positive.</summary>
    public required decimal CurrentBalance { get; set; }

    /// <summary>Whole-percent (0..100). Share of any new <see cref="Deposit"/>
    /// that lands here automatically. Sum across non-archived pockets
    /// should be 100 but is only warned, not enforced.</summary>
    public required decimal AutoSavePercent { get; set; }

    /// <summary>Optional £ target. Progress = CurrentBalance / Goal.</summary>
    public decimal? Goal { get; set; }

    /// <summary>APR as a decimal fraction (0.0525 = 5.25%). Interest is
    /// quoted as APR but accrues/compounds monthly (the convention used
    /// by most consumer auto-saver apps).</summary>
    public decimal AnnualInterestRate { get; set; }

    /// <summary>Optional date the user wants to hit <see cref="Goal"/> by.
    /// Powers the "need £X/mo to hit it" projection and on-track flag.</summary>
    public DateOnly? TargetDate { get; set; }

    public string? Notes { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public int SortOrder { get; set; }
}
