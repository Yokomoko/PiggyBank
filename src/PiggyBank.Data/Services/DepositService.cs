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

        // Pockets that participate in this distribution: positive autosave
        // and either no goal or below it. The goal-pause is intentional — a
        // pocket that's already hit its target shouldn't overflow; its
        // share stays unallocated until the balance drops back below.
        var participating = activePockets
            .Where(p => p.AutoSavePercent > 0m
                     && !(p.Goal is > 0m && p.CurrentBalance >= p.Goal))
            .ToList();

        // Largest-remainder apportionment so the rounded shares sum
        // exactly to the proportional share of the deposit. Per-pocket
        // round-half-away naively (the previous approach) drifts up to
        // half a penny per pocket and compounds — a 7-pocket 100%
        // split of £63.50 came out to £63.52 in production. The fix:
        //   1. Floor each exact share to 2 decimal places.
        //   2. Compute the integer-penny shortfall vs the target total.
        //   3. Hand out the missing pennies, one each, to the pockets
        //      with the largest fractional remainder (apportionment
        //      maths: Hamilton method).
        var targetTotal = Math.Round(
            amount * (participating.Sum(p => p.AutoSavePercent) / 100m),
            2, MidpointRounding.AwayFromZero);

        var entries = participating
            .Select(p =>
            {
                var exact = amount * (p.AutoSavePercent / 100m);
                var floor = Math.Floor(exact * 100m) / 100m;
                return new
                {
                    Pocket = p,
                    Floor = floor,
                    Remainder = exact - floor,
                };
            })
            .ToList();

        var floorSum = entries.Sum(e => e.Floor);
        var pennyDeficit = (int)Math.Round((targetTotal - floorSum) * 100m);

        // Stable tie-break by SortOrder then by original index so the same
        // input always produces the same output (no flaky tests, no
        // user-visible "why does the same deposit allocate differently
        // each time" surprise).
        var ranked = entries
            .Select((e, i) => new { Entry = e, Index = i })
            .OrderByDescending(x => x.Entry.Remainder)
            .ThenBy(x => x.Entry.Pocket.SortOrder)
            .ThenBy(x => x.Index)
            .ToList();

        var bonusByPocket = new Dictionary<Guid, decimal>(entries.Count);
        for (int i = 0; i < ranked.Count; i++)
            bonusByPocket[ranked[i].Entry.Pocket.Id] = i < pennyDeficit ? 0.01m : 0m;

        var allocations = new List<DepositAllocation>(participating.Count);
        var distributed = 0m;
        foreach (var entry in entries)
        {
            var share = entry.Floor + bonusByPocket[entry.Pocket.Id];
            if (share == 0m) continue;

            allocations.Add(new DepositAllocation
            {
                DepositId = deposit.Id,
                PocketId = entry.Pocket.Id,
                AutoSavePercentAtDeposit = entry.Pocket.AutoSavePercent,
                Amount = share,
            });
            entry.Pocket.CurrentBalance += share;
            await pockets.UpdateAsync(entry.Pocket, ct);
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
