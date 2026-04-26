using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class SideIncomeRepository(AppDbContext db, TimeProvider clock) : ISideIncomeRepository
{
    public async Task<IReadOnlyList<SideIncomeEntry>> ListEntriesAsync(CancellationToken ct = default)
        => await db.SideIncomeEntries
            .OrderByDescending(e => e.PaidOn)
            .ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<SideIncomeEntry?> FindEntryAsync(Guid id, CancellationToken ct = default)
        => db.SideIncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<SideIncomeEntry> AddEntryAsync(SideIncomeEntry entry, CancellationToken ct = default)
    {
        if (entry.CreatedAtUtc == default)
            entry.CreatedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.SideIncomeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task UpdateEntryAsync(SideIncomeEntry entry, CancellationToken ct = default)
    {
        db.SideIncomeEntries.Update(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteEntryAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.SideIncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return;
        db.SideIncomeEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SideIncomeAllocation>> ListAllocationsAsync(CancellationToken ct = default)
        => await db.SideIncomeAllocations.ToListAsync(ct);

    public async Task<IReadOnlyList<SideIncomeAllocation>> ListAllocationsForEntryAsync(
        Guid entryId, CancellationToken ct = default)
        => await db.SideIncomeAllocations
            .Where(a => a.SideIncomeEntryId == entryId)
            .ToListAsync(ct);

    public async Task<SideIncomeAllocation> AddAllocationAsync(
        SideIncomeAllocation allocation, CancellationToken ct = default)
    {
        if (allocation.AllocatedAtUtc == default)
            allocation.AllocatedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.SideIncomeAllocations.Add(allocation);
        await db.SaveChangesAsync(ct);
        return allocation;
    }

    public async Task DeleteAllocationAsync(Guid id, CancellationToken ct = default)
    {
        var allocation = await db.SideIncomeAllocations.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (allocation is null) return;
        db.SideIncomeAllocations.Remove(allocation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SideIncomeTemplate>> ListTemplatesAsync(CancellationToken ct = default)
        => await db.SideIncomeTemplates
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<SideIncomeTemplate> AddTemplateAsync(SideIncomeTemplate template, CancellationToken ct = default)
    {
        db.SideIncomeTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.SideIncomeTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return;
        db.SideIncomeTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }
}
