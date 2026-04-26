using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class MonthlyOutgoingRepository(AppDbContext db) : IMonthlyOutgoingRepository
{
    public async Task<IReadOnlyList<MonthlyOutgoing>> ListForMonthAsync(Guid monthId, CancellationToken ct = default)
        => await db.MonthlyOutgoings
            .Where(o => o.MonthId == monthId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(ct);

    public Task<MonthlyOutgoing?> FindAsync(Guid id, CancellationToken ct = default)
        => db.MonthlyOutgoings.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<MonthlyOutgoing> AddAsync(MonthlyOutgoing outgoing, CancellationToken ct = default)
    {
        db.MonthlyOutgoings.Add(outgoing);
        await db.SaveChangesAsync(ct);
        return outgoing;
    }

    public async Task AddRangeAsync(IEnumerable<MonthlyOutgoing> outgoings, CancellationToken ct = default)
    {
        db.MonthlyOutgoings.AddRange(outgoings);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MonthlyOutgoing outgoing, CancellationToken ct = default)
    {
        db.MonthlyOutgoings.Update(outgoing);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.MonthlyOutgoings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"MonthlyOutgoing {id} not found.");
        db.MonthlyOutgoings.Remove(o);
        await db.SaveChangesAsync(ct);
    }
}
