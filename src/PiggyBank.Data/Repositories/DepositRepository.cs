using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class DepositRepository(AppDbContext db) : IDepositRepository
{
    public async Task<IReadOnlyList<Deposit>> ListAsync(int take = 20, CancellationToken ct = default)
        => await db.Deposits
            .OrderByDescending(d => d.DepositedOn)
            .ThenByDescending(d => d.RecordedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<Deposit?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Deposits.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Deposit> AddAsync(Deposit deposit, CancellationToken ct = default)
    {
        db.Deposits.Add(deposit);
        await db.SaveChangesAsync(ct);
        return deposit;
    }

    public async Task<IReadOnlyList<DepositAllocation>> ListAllocationsAsync(Guid depositId, CancellationToken ct = default)
        => await db.DepositAllocations
            .Where(a => a.DepositId == depositId)
            .ToListAsync(ct);

    public async Task AddAllocationsAsync(IEnumerable<DepositAllocation> allocations, CancellationToken ct = default)
    {
        db.DepositAllocations.AddRange(allocations);
        await db.SaveChangesAsync(ct);
    }
}
