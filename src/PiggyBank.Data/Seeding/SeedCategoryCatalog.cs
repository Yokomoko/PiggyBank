using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Seeding;

/// <summary>
/// The 32-category starter list (23 from <c>Template!K2:K23</c> plus 9
/// added 2026-04-22). On first install this populates the
/// system-wide <see cref="SeedCategory"/> table; the profile-creation
/// wizard copies the checked ones into per-profile <see cref="Category"/>.
///
/// <see cref="SeedCategory.DefaultEnabled"/> drives the default-tick
/// state — niche categories like Paint, Sprays, Cashpoint default to
/// unchecked so new profiles don't drown in noise.
/// </summary>
public static class SeedCategoryCatalog
{
    public static IReadOnlyList<SeedCategory> All { get; } = Build();

    private static IReadOnlyList<SeedCategory> Build()
    {
        // ordering here becomes the SortOrder in the DB
        var rows = new (string Name, CategoryKind Kind, bool DefaultEnabled)[]
        {
            // --- Income-ish ---
            ("Contract Work / Bonuses", CategoryKind.Income, true),

            // --- Savings / Overpayments ---
            ("Savings Overpayment", CategoryKind.Overpayment, true),
            ("Auto-Savings", CategoryKind.Savings, true),                        // NEW
            ("Credit Card Overpayment", CategoryKind.Overpayment, true),
            ("Car Overpayment", CategoryKind.Overpayment, true),
            ("Mortgage Overpayment", CategoryKind.Overpayment, false),
            ("Phone Overpayment", CategoryKind.Overpayment, false),
            ("Gym Overpayment", CategoryKind.Overpayment, false),

            // --- Household & bills ---
            ("Broadband", CategoryKind.Spend, true),
            ("Subscriptions", CategoryKind.Spend, true),                         // NEW

            // --- Food ---
            ("Groceries", CategoryKind.Spend, true),                             // NEW
            ("Eating Out / Takeaway", CategoryKind.Spend, true),                 // NEW
            ("Food / Drink", CategoryKind.Spend, true),  // legacy catch-all

            // --- Transport ---
            ("Petrol", CategoryKind.Spend, true),
            ("Public Transport", CategoryKind.Spend, true),                      // NEW
            ("Car Related", CategoryKind.Spend, true),

            // --- Lifestyle ---
            ("Clothing", CategoryKind.Spend, true),
            ("Wardrobe", CategoryKind.Spend, false),    // overlaps with Clothing
            ("Cinema", CategoryKind.Spend, true),
            ("Tech/Games", CategoryKind.Spend, true),
            ("Holiday", CategoryKind.Spend, true),                               // NEW

            // --- Health & care ---
            ("Health / Medical", CategoryKind.Spend, true),                      // NEW
            ("Medication/Tablets", CategoryKind.Spend, false),  // subsumed by Health for most

            // --- Home ---
            ("Home Improvement", CategoryKind.Spend, true),                      // NEW
            ("Paint", CategoryKind.Spend, false),       // subsumed by Home Improvement

            // --- Other ---
            ("Dogs", CategoryKind.Spend, true),
            ("Christmas / Gifts", CategoryKind.Spend, true),
            ("Donations / Charity", CategoryKind.Spend, true),                   // NEW
            ("Lend Money", CategoryKind.Spend, false),
            ("Cashpoint", CategoryKind.Spend, false),   // legacy
            ("Sprays / Product", CategoryKind.Spend, false),  // legacy niche
            ("General/Other", CategoryKind.Spend, true),
        };

        var list = new List<SeedCategory>(rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            list.Add(new SeedCategory
            {
                Name = r.Name,
                Kind = r.Kind,
                DefaultEnabled = r.DefaultEnabled,
                SortOrder = i,
            });
        }
        return list;
    }
}
