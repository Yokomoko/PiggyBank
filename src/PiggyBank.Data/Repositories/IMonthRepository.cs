using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IMonthRepository
{
    Task<IReadOnlyList<Month>> ListAsync(CancellationToken ct = default);
    Task<Month?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Month?> FindOpenAsync(CancellationToken ct = default);
    Task<Month?> FindForDateAsync(DateOnly date, CancellationToken ct = default);
    Task<Month?> FindPriorToAsync(DateOnly periodStart, CancellationToken ct = default);
    Task<Month> AddAsync(Month month, CancellationToken ct = default);
    Task UpdateAsync(Month month, CancellationToken ct = default);
    Task CloseAsync(Guid id, CancellationToken ct = default);
}
