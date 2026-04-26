using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class DebtRepository(AppDbContext db, TimeProvider clock) : IDebtRepository
{
    public async Task<IReadOnlyList<Debt>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = db.Debts.AsQueryable();
        if (!includeArchived) q = q.Where(d => d.ArchivedAtUtc == null);
        return await q.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(ct);
    }

    public Task<Debt?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Debts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Debt> AddAsync(Debt debt, CancellationToken ct = default)
    {
        db.Debts.Add(debt);
        await db.SaveChangesAsync(ct);
        return debt;
    }

    public async Task UpdateAsync(Debt debt, CancellationToken ct = default)
    {
        db.Debts.Update(debt);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Debts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Debt {id} not found.");
        d.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DebtWithLatestBalance>> ListWithLatestBalancesAsync(
        bool includeArchived = false, CancellationToken ct = default)
    {
        var debts = await ListAsync(includeArchived, ct);

        // One query per list to keep EF + SQLite happy — profiles rarely have
        // more than ~10 debts so the N+1 is negligible.
        var results = new List<DebtWithLatestBalance>(debts.Count);
        foreach (var debt in debts)
        {
            var latest = await db.DebtSnapshots
                .Where(s => s.DebtId == debt.Id)
                .OrderByDescending(s => s.SnapshotDate)
                .FirstOrDefaultAsync(ct);

            results.Add(new DebtWithLatestBalance(
                debt,
                latest?.Balance ?? debt.OpeningBalance,
                latest?.SnapshotDate));
        }
        return results;
    }

    public async Task<IReadOnlyList<DebtSnapshot>> ListSnapshotsAsync(Guid debtId, CancellationToken ct = default)
        => await db.DebtSnapshots
            .Where(s => s.DebtId == debtId)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(ct);

    public async Task<DebtSnapshot> AddSnapshotAsync(DebtSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.RecordedAtUtc == default)
            snapshot.RecordedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.DebtSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    public async Task DeleteSnapshotAsync(Guid snapshotId, CancellationToken ct = default)
    {
        var s = await db.DebtSnapshots.FirstOrDefaultAsync(x => x.Id == snapshotId, ct)
            ?? throw new InvalidOperationException($"DebtSnapshot {snapshotId} not found.");
        db.DebtSnapshots.Remove(s);
        await db.SaveChangesAsync(ct);
    }
}
