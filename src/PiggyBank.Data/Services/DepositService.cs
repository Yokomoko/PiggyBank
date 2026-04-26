using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.Data.Services;

/// <summary>
/// Records a <see cref="Deposit"/> and distributes the amount across
/// non-archived <see cref="Pocket"/>s according to each pocket's
/// <see cref="Pocket.AutoSavePercent"/>. Snapshots the autosave
/// percentage onto each <see cref="DepositAllocation"/> so the history
/// is stable even if the user later rebalances their autosave.
/// </summary>
/// <remarks>
/// Autosave percentages are not normalised — if the sum is 80, only
/// 80% of the deposit is allocated (matches how typical auto-saver
/// apps behave: leftover stays in the "primary pocket" with 0% autosave
/// if the user has one). If the sum is over 100, we still apply each
/// pocket's raw percentage, which means the distributed total can exceed
/// the deposit amount. The UI warns when the sum isn't 100; the service
/// doesn't second-guess.
/// </remarks>
public sealed class DepositService(
    AppDbContext db,
    IPocketRepository pockets,
    IDepositRepository deposits,
    TimeProvider clock)
{
    public async Task<DepositResult> RecordAsync(
        DateOnly depositedOn,
        decimal amount,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentException("Deposit amount must be positive.", nameof(amount));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var deposit = await deposits.AddAsync(new Deposit
        {
            DepositedOn = depositedOn,
            Amount = amount,
            Notes = notes,
            RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
        }, ct);

        var activePockets = await pockets.ListAsync(ct: ct);
        var allocations = new List<DepositAllocation>(activePockets.Count);
        var distributed = 0m;
        foreach (var pocket in activePockets)
        {
            if (pocket.AutoSavePercent <= 0m) continue;
            // Pause autosave on goal-reached pockets. The user's autosave %
            // stays untouched in the DB so if they later withdraw from the
            // pocket (balance drops below goal) it naturally re-engages.
            // The effect is that this pocket's share goes unallocated rather
            // than overflowing past the goal.
            if (pocket.Goal is > 0m && pocket.CurrentBalance >= pocket.Goal) continue;
            var share = Math.Round(
                amount * (pocket.AutoSavePercent / 100m),
                2,
                MidpointRounding.AwayFromZero);
            if (share == 0m) continue;

            allocations.Add(new DepositAllocation
            {
                DepositId = deposit.Id,
                PocketId = pocket.Id,
                AutoSavePercentAtDeposit = pocket.AutoSavePercent,
                Amount = share,
            });
            pocket.CurrentBalance += share;
            await pockets.UpdateAsync(pocket, ct);
            distributed += share;
        }

        if (allocations.Count > 0)
            await deposits.AddAllocationsAsync(allocations, ct);

        await tx.CommitAsync(ct);

        return new DepositResult(deposit, allocations, amount - distributed);
    }

    /// <summary>Records a <see cref="Deposit"/> that lands 100% in one
    /// pocket. Skips the autosave distribution AND the goal-reached guard
    /// — the user has explicitly picked this pocket so they get what they
    /// asked for, overflow included. <see cref="DepositAllocation.AutoSavePercentAtDeposit"/>
    /// is snapshotted as 100 to distinguish direct deposits in history.</summary>
    public async Task<DepositResult> RecordToPocketAsync(
        Guid pocketId,
        DateOnly depositedOn,
        decimal amount,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentException("Deposit amount must be positive.", nameof(amount));
        var pocket = await pockets.FindAsync(pocketId, ct)
            ?? throw new InvalidOperationException($"Pocket {pocketId} not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var deposit = await deposits.AddAsync(new Deposit
        {
            DepositedOn = depositedOn,
            Amount = amount,
            Notes = notes,
            RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
        }, ct);

        var allocation = new DepositAllocation
        {
            DepositId = deposit.Id,
            PocketId = pocket.Id,
            AutoSavePercentAtDeposit = 100m,
            Amount = amount,
        };
        pocket.CurrentBalance += amount;
        await pockets.UpdateAsync(pocket, ct);
        await deposits.AddAllocationsAsync(new[] { allocation }, ct);

        await tx.CommitAsync(ct);

        return new DepositResult(deposit, new[] { allocation }, 0m);
    }
}

public sealed record DepositResult(
    Deposit Deposit,
    IReadOnlyList<DepositAllocation> Allocations,
    decimal Unallocated);
