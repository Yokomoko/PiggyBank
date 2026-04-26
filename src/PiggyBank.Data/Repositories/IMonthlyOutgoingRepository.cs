using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IMonthlyOutgoingRepository
{
    Task<IReadOnlyList<MonthlyOutgoing>> ListForMonthAsync(Guid monthId, CancellationToken ct = default);
    Task<MonthlyOutgoing?> FindAsync(Guid id, CancellationToken ct = default);
    Task<MonthlyOutgoing> AddAsync(MonthlyOutgoing outgoing, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<MonthlyOutgoing> outgoings, CancellationToken ct = default);
    Task UpdateAsync(MonthlyOutgoing outgoing, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
