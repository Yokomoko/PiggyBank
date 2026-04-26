using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IPocketRepository
{
    Task<IReadOnlyList<Pocket>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<Pocket?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Pocket> AddAsync(Pocket pocket, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Pocket> pockets, CancellationToken ct = default);
    Task UpdateAsync(Pocket pocket, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sum of non-archived <see cref="Pocket.AutoSavePercent"/>.
    /// UI warns the user when this isn't 100 but doesn't block — some
    /// providers happily accept &lt;100 and park the remainder in the
    /// primary pocket.</summary>
    Task<decimal> GetAutoSaveSumAsync(CancellationToken ct = default);
}
