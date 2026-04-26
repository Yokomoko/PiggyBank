using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IDepositRepository
{
    Task<IReadOnlyList<Deposit>> ListAsync(int take = 20, CancellationToken ct = default);
    Task<Deposit?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Deposit> AddAsync(Deposit deposit, CancellationToken ct = default);

    Task<IReadOnlyList<DepositAllocation>> ListAllocationsAsync(Guid depositId, CancellationToken ct = default);
    Task AddAllocationsAsync(IEnumerable<DepositAllocation> allocations, CancellationToken ct = default);
}
