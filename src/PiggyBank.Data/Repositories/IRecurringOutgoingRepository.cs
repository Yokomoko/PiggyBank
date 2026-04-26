using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IRecurringOutgoingRepository
{
    Task<IReadOnlyList<RecurringOutgoing>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<RecurringOutgoing?> FindAsync(Guid id, CancellationToken ct = default);
    Task<RecurringOutgoing> AddAsync(RecurringOutgoing outgoing, CancellationToken ct = default);
    Task UpdateAsync(RecurringOutgoing outgoing, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);
}
