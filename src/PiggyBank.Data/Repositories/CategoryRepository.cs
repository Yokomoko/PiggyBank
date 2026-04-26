using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public sealed class CategoryRepository(AppDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = db.Categories.AsQueryable();
        if (!includeArchived) q = q.Where(c => !c.IsArchived);
        return await q.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<Category?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Category> AddAsync(string name, CategoryKind kind, string? colourHex = null, CancellationToken ct = default)
    {
        var cat = new Category { Name = name.Trim(), Kind = kind };
        if (!string.IsNullOrWhiteSpace(colourHex)) cat.ColourHex = colourHex;
        db.Categories.Add(cat);
        await db.SaveChangesAsync(ct);
        return cat;
    }

    public async Task UpdateAsync(Category category, CancellationToken ct = default)
    {
        db.Categories.Update(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Category {id} not found.");
        cat.IsArchived = true;
        await db.SaveChangesAsync(ct);
    }
}
