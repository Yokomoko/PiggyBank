using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class RecurringOutgoingRepository(AppDbContext db) : IRecurringOutgoingRepository
{
    public async Task<IReadOnlyList<RecurringOutgoing>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = db.RecurringOutgoings.AsQueryable();
        if (!includeArchived) q = q.Where(r => !r.IsArchived);
        return await q.OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
    }

    public Task<RecurringOutgoing?> FindAsync(Guid id, CancellationToken ct = default)
        => db.RecurringOutgoings.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<RecurringOutgoing> AddAsync(RecurringOutgoing outgoing, CancellationToken ct = default)
    {
        db.RecurringOutgoings.Add(outgoing);
        await db.SaveChangesAsync(ct);
        return outgoing;
    }

    public async Task UpdateAsync(RecurringOutgoing outgoing, CancellationToken ct = default)
    {
        db.RecurringOutgoings.Update(outgoing);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.RecurringOutgoings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"RecurringOutgoing {id} not found.");
        r.IsArchived = true;
        await db.SaveChangesAsync(ct);
    }
}
