using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class PocketRepository(AppDbContext db, TimeProvider clock) : IPocketRepository
{
    public async Task<IReadOnlyList<Pocket>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = db.Pockets.AsQueryable();
        if (!includeArchived) q = q.Where(p => p.ArchivedAtUtc == null);
        return await q.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);
    }

    public Task<Pocket?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Pockets.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Pocket> AddAsync(Pocket pocket, CancellationToken ct = default)
    {
        db.Pockets.Add(pocket);
        await db.SaveChangesAsync(ct);
        return pocket;
    }

    public async Task AddRangeAsync(IEnumerable<Pocket> pockets, CancellationToken ct = default)
    {
        db.Pockets.AddRange(pockets);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Pocket pocket, CancellationToken ct = default)
    {
        db.Pockets.Update(pocket);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Pockets.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Pocket {id} not found.");
        p.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetAutoSaveSumAsync(CancellationToken ct = default)
    {
        return await db.Pockets
            .Where(p => p.ArchivedAtUtc == null)
            .SumAsync(p => (decimal?)p.AutoSavePercent, ct) ?? 0m;
    }
}
