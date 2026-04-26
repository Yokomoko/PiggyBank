using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class TransactionRepository(AppDbContext db) : ITransactionRepository
{
    public async Task<IReadOnlyList<Transaction>> ListForMonthAsync(Guid monthId, CancellationToken ct = default)
        => await db.Transactions
            .Where(t => t.MonthId == monthId)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .ToListAsync(ct);

    public async Task<decimal> SumForMonthAsync(Guid monthId, bool includeFuture = false, CancellationToken ct = default)
    {
        // includeFuture parameter retained for backwards compat, no-op now
        // that IsFuture has been dropped from the entity. Will be removed
        // when the interface is next revisited.
        var q = db.Transactions.Where(t => t.MonthId == monthId);
        return await q.SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
    }

    public async Task<IReadOnlyList<CategorySum>> SumByCategoryForMonthAsync(Guid monthId, CancellationToken ct = default)
    {
        // LEFT JOIN via GroupJoin to include "Uncategorised" bucket (null CategoryId).
        var results = await db.Transactions
            .Where(t => t.MonthId == monthId)
            .GroupBy(t => t.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Total = g.Sum(t => t.Amount),
            })
            .ToListAsync(ct);

        var categoryNames = await db.Categories
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return results
            .Select(r => new CategorySum(
                r.CategoryId,
                r.CategoryId is null ? "Uncategorised"
                    : categoryNames.GetValueOrDefault(r.CategoryId.Value, "Unknown"),
                r.Total))
            .OrderBy(s => s.CategoryName)
            .ToList();
    }

    public Task<Transaction?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken ct = default)
    {
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        return transaction;
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken ct = default)
    {
        db.Transactions.Update(transaction);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");
        db.Transactions.Remove(t);
        await db.SaveChangesAsync(ct);
    }
}
