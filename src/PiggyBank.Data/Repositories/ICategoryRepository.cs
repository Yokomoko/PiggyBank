using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<Category?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Category> AddAsync(string name, CategoryKind kind, string? colourHex = null, CancellationToken ct = default);
    Task UpdateAsync(Category category, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);
}
