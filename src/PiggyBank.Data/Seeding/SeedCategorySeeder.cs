using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Seeding;

/// <summary>
/// Idempotently ensures the system-wide <see cref="SeedCategory"/> table
/// matches <see cref="SeedCategoryCatalog.All"/>. Run on app startup.
/// Uses <c>Name</c> as the merge key so reordering the catalog doesn't
/// duplicate rows, and existing rows get their <c>SortOrder</c> /
/// <c>DefaultEnabled</c> updated to match the latest catalog.
/// </summary>
public sealed class SeedCategorySeeder(AppDbContext db)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existing = await db.SeedCategories.ToListAsync(ct);
        var byName = existing.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var canonical in SeedCategoryCatalog.All)
        {
            if (byName.TryGetValue(canonical.Name, out var row))
            {
                row.Kind = canonical.Kind;
                row.SortOrder = canonical.SortOrder;
                row.DefaultEnabled = canonical.DefaultEnabled;
            }
            else
            {
                db.SeedCategories.Add(new SeedCategory
                {
                    Name = canonical.Name,
                    Kind = canonical.Kind,
                    SortOrder = canonical.SortOrder,
                    DefaultEnabled = canonical.DefaultEnabled,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
