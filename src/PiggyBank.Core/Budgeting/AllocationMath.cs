using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Budgeting;

/// <summary>
/// Pure functions over monthly outgoings + ledger transactions for the
/// allocation drawdown / overspend logic. No IO, no state — the Current
/// Month VM calls <see cref="Compute"/> to size the month's effective
/// outgoings and the spend total that excludes drawdowns.
///
/// Conventions: outgoings are stored negative (spend) or positive (income);
/// ledger transactions follow the same sign convention. <c>Math.Abs(o.Amount)</c>
/// gives the planned magnitude for an allocation.
/// </summary>
public static class AllocationMath
{
    /// <summary>
    /// Compute the effective month figures for a set of outgoings + ledger
    /// transactions. The algorithm:
    ///   1) Find allocation categories (tied to an IsAllocation outgoing
    ///      that has a CategoryId).
    ///   2) Per allocation: <c>Used</c> = magnitude of ledger transactions
    ///      tagged with that category. <c>Remaining</c> = Allocated − Used
    ///      (can go negative when overspent). Effective outgoing = the more
    ///      negative of (planned, −Used). Overspend raises THIS month's
    ///      effective outgoing without mutating any template.
    ///   3) Non-allocation outgoings always contribute their planned Amount.
    ///   4) <c>MonthlySpendTotal</c> excludes drawdown transactions —
    ///      they've already been counted via the allocation outgoing's
    ///      effective amount.
    /// </summary>
    public static AllocationResult Compute(
        IReadOnlyList<MonthlyOutgoing> outgoings,
        IReadOnlyList<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(outgoings);
        ArgumentNullException.ThrowIfNull(transactions);

        var allocationCategoryIds = outgoings
            .Where(o => o.IsAllocation && o.CategoryId.HasValue)
            .Select(o => o.CategoryId!.Value)
            .ToHashSet();

        bool IsDrawdown(Transaction t) =>
            t.CategoryId.HasValue && allocationCategoryIds.Contains(t.CategoryId.Value);

        decimal totalOutgoings = 0m;
        var details = new List<AllocationDetail>(outgoings.Count);
        foreach (var o in outgoings)
        {
            if (o.IsAllocation && o.CategoryId.HasValue)
            {
                var used = Math.Abs(transactions
                    .Where(t => t.CategoryId == o.CategoryId)
                    .Sum(t => t.Amount));
                var allocated = Math.Abs(o.Amount);
                var isOverspent = used > allocated;
                // Overspend raises the month's effective outgoing (more negative).
                var effective = isOverspent ? -used : o.Amount;
                totalOutgoings += effective;
                details.Add(new AllocationDetail(
                    OutgoingId: o.Id,
                    CategoryId: o.CategoryId,
                    Allocated: allocated,
                    Used: used,
                    Remaining: allocated - used,
                    IsOverspent: isOverspent,
                    EffectiveAmount: effective));
            }
            else
            {
                totalOutgoings += o.Amount;
                details.Add(new AllocationDetail(
                    OutgoingId: o.Id,
                    CategoryId: o.CategoryId,
                    Allocated: 0m,
                    Used: 0m,
                    Remaining: 0m,
                    IsOverspent: false,
                    EffectiveAmount: o.Amount));
            }
        }

        var monthlySpendTotal = transactions.Where(t => !IsDrawdown(t)).Sum(t => t.Amount);

        return new AllocationResult(totalOutgoings, monthlySpendTotal, details);
    }
}

/// <summary>
/// Result of <see cref="AllocationMath.Compute"/>. <see cref="TotalOutgoings"/>
/// is the sum of effective outgoing amounts (negative for spend, positive for
/// wages/income). <see cref="MonthlySpendTotal"/> is the sum of ledger
/// transactions that are NOT drawdowns against an allocation. <see cref="Details"/>
/// is per-outgoing — same order as the input list.
/// </summary>
public sealed record AllocationResult(
    decimal TotalOutgoings,
    decimal MonthlySpendTotal,
    IReadOnlyList<AllocationDetail> Details);

/// <summary>
/// Per-outgoing breakdown. For non-allocation rows, <see cref="Allocated"/>,
/// <see cref="Used"/>, <see cref="Remaining"/> are zero, <see cref="IsOverspent"/>
/// is false, and <see cref="EffectiveAmount"/> equals the planned <c>Amount</c>.
/// </summary>
public sealed record AllocationDetail(
    Guid OutgoingId,
    Guid? CategoryId,
    decimal Allocated,
    decimal Used,
    decimal Remaining,
    bool IsOverspent,
    decimal EffectiveAmount);
