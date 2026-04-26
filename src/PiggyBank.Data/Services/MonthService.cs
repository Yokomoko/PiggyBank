using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.Data.Services;

/// <summary>
/// Orchestrates the workflow around <see cref="Month"/> creation, closure,
/// and rollover. Keeps the ViewModel layer out of multi-table transaction
/// plumbing.
/// </summary>
/// <remarks>
/// Rollover is MANUAL. Creating a new month defaults
/// <see cref="Month.CarriedOverBalance"/> to zero. The user may spend the
/// first few days of a new pay cycle still entering the tail-end of the
/// prior month's spends, so auto-computing a carry-over at creation time
/// would stamp in a wrong value. Use
/// <see cref="ApplyRolloverFromPriorAsync"/> when the user explicitly
/// confirms the prior month is finalised.
/// </remarks>
public sealed class MonthService(
    AppDbContext db,
    IMonthRepository months,
    IMonthlyOutgoingRepository monthlyOutgoings,
    IRecurringOutgoingRepository recurringOutgoings,
    ITransactionRepository transactions)
{
    /// <summary>
    /// Creates a new month and snapshots active recurring outgoings into
    /// monthly outgoings. <see cref="Month.CarriedOverBalance"/> defaults
    /// to <paramref name="carryOverOverride"/> if supplied, otherwise zero
    /// — it is NEVER auto-computed from the prior month. One transaction.
    /// </summary>
    public async Task<Month> CreateAsync(
        DateOnly lastPayday,
        DateOnly nextPayday,
        decimal carryOverOverride = 0m,
        CancellationToken ct = default)
    {
        if (nextPayday <= lastPayday)
            throw new ArgumentException("Next payday must be after last payday.", nameof(nextPayday));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var month = new Month
        {
            PeriodStart = lastPayday,
            PeriodEnd = nextPayday.AddDays(-1),
            LastPayday = lastPayday,
            NextPayday = nextPayday,
            CarriedOverBalance = carryOverOverride,
        };

        await months.AddAsync(month, ct);

        // Snapshot recurring outgoings into this month.
        var templates = await recurringOutgoings.ListAsync(ct: ct);
        var order = 0;
        var snapshots = templates.Select(t => new MonthlyOutgoing
        {
            MonthId = month.Id,
            RecurringOutgoingId = t.Id,
            Name = t.Name,
            Amount = t.DefaultAmount,
            CategoryId = t.CategoryId,
            IsWage = t.IsWage,
            SortOrder = order++,
        }).ToList();

        if (snapshots.Count > 0)
            await monthlyOutgoings.AddRangeAsync(snapshots, ct);

        await tx.CommitAsync(ct);
        return month;
    }

    /// <summary>
    /// Returns the prior month's closing balance as a suggested rollover
    /// figure WITHOUT applying it. Callers present this to the user for
    /// explicit confirmation before calling
    /// <see cref="ApplyRolloverFromPriorAsync"/>. Returns null when no
    /// prior month exists or it's still open (figures not final).
    /// </summary>
    public async Task<decimal?> SuggestRolloverAsync(Guid monthId, CancellationToken ct = default)
    {
        var month = await months.FindAsync(monthId, ct)
            ?? throw new InvalidOperationException($"Month {monthId} not found.");

        var prior = await months.FindPriorToAsync(month.PeriodStart, ct);
        if (prior is null) return null;

        // Only suggest from a closed month — open prior months still change.
        if (!prior.IsClosed) return null;

        return await ComputeClosingBalanceAsync(prior, ct);
    }

    /// <summary>
    /// Explicitly applies the prior month's closing balance as this month's
    /// carry-over. Overwrites any existing value. The user is expected to
    /// have reviewed the figure first via <see cref="SuggestRolloverAsync"/>.
    /// </summary>
    public async Task<decimal> ApplyRolloverFromPriorAsync(Guid monthId, CancellationToken ct = default)
    {
        var month = await months.FindAsync(monthId, ct)
            ?? throw new InvalidOperationException($"Month {monthId} not found.");

        var prior = await months.FindPriorToAsync(month.PeriodStart, ct)
            ?? throw new InvalidOperationException("No prior month to roll over from.");

        if (!prior.IsClosed)
            throw new InvalidOperationException(
                "Prior month is still open. Close it first to finalise the closing balance.");

        var closing = await ComputeClosingBalanceAsync(prior, ct);
        month.CarriedOverBalance = closing;
        await months.UpdateAsync(month, ct);
        return closing;
    }

    /// <summary>
    /// Closing balance = Total (sum of outgoings incl. wage) + carry-in
    /// - monthly spend.
    /// </summary>
    public async Task<decimal> ComputeClosingBalanceAsync(Month month, CancellationToken ct = default)
    {
        var outgoings = await monthlyOutgoings.ListForMonthAsync(month.Id, ct);
        var total = outgoings.Sum(o => o.Amount);
        var spent = await transactions.SumForMonthAsync(month.Id, includeFuture: false, ct: ct);
        return total - spent + month.CarriedOverBalance;
    }

    /// <summary>
    /// Closes the month in the audit sense — no more auto-edits. This is
    /// the trigger that makes <see cref="SuggestRolloverAsync"/> return a
    /// non-null figure for the next month.
    /// </summary>
    public Task CloseAsync(Guid monthId, CancellationToken ct = default)
        => months.CloseAsync(monthId, ct);
}
