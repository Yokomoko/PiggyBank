using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class MonthRepository(AppDbContext db) : IMonthRepository
{
    public async Task<IReadOnlyList<Month>> ListAsync(CancellationToken ct = default)
        => await db.Months.OrderByDescending(m => m.PeriodStart).ToListAsync(ct);

    public Task<Month?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Months.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<Month?> FindOpenAsync(CancellationToken ct = default)
        => db.Months.Where(m => !m.IsClosed)
                    .OrderByDescending(m => m.PeriodStart)
                    .FirstOrDefaultAsync(ct);

    public Task<Month?> FindForDateAsync(DateOnly date, CancellationToken ct = default)
        => db.Months.FirstOrDefaultAsync(m => m.PeriodStart <= date && date <= m.PeriodEnd, ct);

    public Task<Month?> FindPriorToAsync(DateOnly periodStart, CancellationToken ct = default)
        => db.Months.Where(m => m.PeriodStart < periodStart)
                    .OrderByDescending(m => m.PeriodStart)
                    .FirstOrDefaultAsync(ct);

    public async Task<Month> AddAsync(Month month, CancellationToken ct = default)
    {
        db.Months.Add(month);
        await db.SaveChangesAsync(ct);
        return month;
    }

    public async Task UpdateAsync(Month month, CancellationToken ct = default)
    {
        db.Months.Update(month);
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.Months.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Month {id} not found.");
        m.IsClosed = true;
        await db.SaveChangesAsync(ct);
    }
}
