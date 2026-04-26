using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.Data.Services;

/// <summary>
/// Orchestrates moving a <see cref="SideIncomeEntry"/>'s value into either
/// a <see cref="Pocket"/> (direct deposit) or the main ledger (income
/// <see cref="Transaction"/> in the payday <see cref="Month"/> containing
/// the entry's PaidOn). Keeps cross-cutting rules in one place: the
/// remaining-unallocated ceiling, the closed-month guard, and wiring the
/// resulting Deposit/Transaction IDs into the allocation record for audit.
/// </summary>
public sealed class SideIncomeService(
    AppDbContext db,
    ISideIncomeRepository sideIncome,
    IMonthRepository months,
    ITransactionRepository transactions,
    DepositService deposits)
{
    /// <summary>Allocates <paramref name="amount"/> of the entry into
    /// <paramref name="pocketId"/> as a direct deposit (bypasses autosave
    /// distribution + goal-reached guard). Rejects over-allocation.</summary>
    public async Task<SideIncomeAllocation> AllocateToPocketAsync(
        Guid entryId,
        Guid pocketId,
        decimal amount,
        string? notes = null,
        CancellationToken ct = default)
    {
        var entry = await sideIncome.FindEntryAsync(entryId, ct)
            ?? throw new InvalidOperationException($"Side-income entry {entryId} not found.");
        await GuardAmountAsync(entry, amount, ct);

        // DepositService opens its own transaction. Nesting another here
        // would trip "EnsureNoTransactions" on SQLite. The allocation-add
        // happens sequentially — if it fails, the deposit stands on its
        // own (money is actually in the pocket; re-running the allocation
        // link can be a manual fix, but loss of the pocket deposit would
        // be far worse).
        var depositResult = await deposits.RecordToPocketAsync(
            pocketId,
            entry.PaidOn,
            amount,
            notes ?? $"Side income — {(entry.Description ?? entry.PaidOn.ToString("dd MMM yyyy"))}",
            ct);

        var allocation = await sideIncome.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = entryId,
            Amount = amount,
            Target = SideIncomeAllocationTarget.Pocket,
            PocketId = pocketId,
            PocketDepositId = depositResult.Deposit.Id,
            Notes = notes,
        }, ct);

        return allocation;
    }

    /// <summary>Allocates <paramref name="amount"/> of the entry into the
    /// main ledger by creating a positive income <see cref="Transaction"/>
    /// in the payday <see cref="Month"/> whose period contains PaidOn.
    /// Throws if no matching month exists or the matching month is closed.</summary>
    public async Task<SideIncomeAllocation> AllocateToMainLedgerAsync(
        Guid entryId,
        decimal amount,
        string? notes = null,
        CancellationToken ct = default)
    {
        var entry = await sideIncome.FindEntryAsync(entryId, ct)
            ?? throw new InvalidOperationException($"Side-income entry {entryId} not found.");
        await GuardAmountAsync(entry, amount, ct);

        var month = await months.FindForDateAsync(entry.PaidOn, ct)
            ?? throw new InvalidOperationException(
                $"No open payday month contains {entry.PaidOn:dd MMM yyyy}. "
                + "Start that month first, or allocate to a pocket instead.");
        if (month.IsClosed)
            throw new InvalidOperationException(
                $"The payday month for {entry.PaidOn:dd MMM yyyy} is closed. "
                + "Unlock it or allocate to a pocket instead.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ledgerTx = new Transaction
        {
            MonthId = month.Id,
            Date = entry.PaidOn,
            Payee = entry.Description ?? "Side income",
            Amount = Math.Abs(amount),  // income = positive
        };
        await transactions.AddAsync(ledgerTx, ct);

        var allocation = await sideIncome.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = entryId,
            Amount = amount,
            Target = SideIncomeAllocationTarget.MainLedger,
            MonthId = month.Id,
            LedgerTransactionId = ledgerTx.Id,
            Notes = notes,
        }, ct);

        await tx.CommitAsync(ct);
        return allocation;
    }

    private async Task GuardAmountAsync(SideIncomeEntry entry, decimal amount, CancellationToken ct)
    {
        if (amount <= 0m)
            throw new ArgumentException("Allocation amount must be positive.", nameof(amount));

        var existing = await sideIncome.ListAllocationsForEntryAsync(entry.Id, ct);
        var remaining = SideIncomeMath.RemainingFor(entry, existing);
        if (amount > remaining)
        {
            // Hard-coded en-GB so the message renders £ regardless of the OS
            // culture — CI runners default to en-US, which would say $20.00
            // and break the unit test that asserts on the £-formatted text.
            var gb = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            throw new InvalidOperationException(
                $"Can't allocate {amount.ToString("C2", gb)} — only {remaining.ToString("C2", gb)} remains on this entry.");
        }
    }
}
