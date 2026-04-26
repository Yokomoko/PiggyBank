using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface ITransactionRepository
{
    Task<IReadOnlyList<Transaction>> ListForMonthAsync(Guid monthId, CancellationToken ct = default);
    Task<decimal> SumForMonthAsync(Guid monthId, bool includeFuture = false, CancellationToken ct = default);
    Task<IReadOnlyList<CategorySum>> SumByCategoryForMonthAsync(Guid monthId, CancellationToken ct = default);
    Task<Transaction?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record CategorySum(Guid? CategoryId, string? CategoryName, decimal Total);
