using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface ISideIncomeRepository
{
    Task<IReadOnlyList<SideIncomeEntry>> ListEntriesAsync(CancellationToken ct = default);
    Task<SideIncomeEntry?> FindEntryAsync(Guid id, CancellationToken ct = default);
    Task<SideIncomeEntry> AddEntryAsync(SideIncomeEntry entry, CancellationToken ct = default);
    Task UpdateEntryAsync(SideIncomeEntry entry, CancellationToken ct = default);
    Task DeleteEntryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SideIncomeAllocation>> ListAllocationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SideIncomeAllocation>> ListAllocationsForEntryAsync(Guid entryId, CancellationToken ct = default);
    Task<SideIncomeAllocation> AddAllocationAsync(SideIncomeAllocation allocation, CancellationToken ct = default);
    Task DeleteAllocationAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SideIncomeTemplate>> ListTemplatesAsync(CancellationToken ct = default);
    Task<SideIncomeTemplate> AddTemplateAsync(SideIncomeTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}
