namespace PiggyBank.Core.Entities;

/// <summary>
/// Per-month snapshot of a bill / income line. Captured at month-open
/// time from the <see cref="RecurringOutgoing"/> template, then free to
/// drift per-month. This is the key historical-integrity choice: editing
/// a template doesn't rewrite history.
/// </summary>
public sealed class MonthlyOutgoing : ProfileOwnedEntity
{
    public required Guid MonthId { get; set; }

    /// <summary>Null when user manually added a one-off outgoing this month
    /// (e.g. a gym joining fee) that has no template.</summary>
    public Guid? RecurringOutgoingId { get; set; }

    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsWage { get; set; }

    /// <summary>When true this outgoing is an "allocation" — the user has set
    /// £X aside for this category (e.g. Fuel £150), and ledger transactions
    /// tagged with the same CategoryId DRAW DOWN from that pool instead of
    /// adding to MonthlySpendTotal. Overspend raises the month's effective
    /// amount but leaves the recurring template unchanged. Setting this flag
    /// auto-creates a matching Category if one doesn't exist.</summary>
    public bool IsAllocation { get; set; }

    public int SortOrder { get; set; }
}
