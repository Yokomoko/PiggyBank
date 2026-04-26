using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.Data.Tests.Tenancy;

/// <summary>
/// Drives every repository's "list" method and asserts the returned data
/// belongs to the calling profile only. Complement to
/// <see cref="TenantLeakTests"/> which works at the DbSet level — this
/// proves the repository wrappers inherit the same isolation.
/// </summary>
public sealed class RepositoryTenantLeakTests
{
    [Fact]
    public async Task Repositories_never_return_rows_from_other_profile()
    {
        using var db = TestDb.CreateAdmin();
        var a = new Profile { DisplayName = "Alex" };
        var b = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(a, b);
        await db.Context.SaveChangesAsync();

        // Seed distinct data in each profile.
        db.SwitchToProfile(a.Id);
        await SeedProfileAsync(db, "A");

        db.SwitchToProfile(b.Id);
        await SeedProfileAsync(db, "B");

        // Now query every repository's list method from B's scope
        // and assert nothing with "A" in the name leaks through.
        var cats = await new CategoryRepository(db.Context).ListAsync();
        cats.Should().OnlyContain(c => !c.Name.Contains("_A"));

        var recurring = await new RecurringOutgoingRepository(db.Context).ListAsync();
        recurring.Should().OnlyContain(r => !r.Name.Contains("_A"));

        var months = await new MonthRepository(db.Context).ListAsync();
        months.Should().OnlyContain(m => m.Notes != "_A");

        // Month-scoped queries — use B's month id only. If we gave them A's id
        // from B's scope, the repo would return nothing (filter stops it), which
        // is a correct-by-design behaviour.
        var bMonth = months.Single();
        var outgoings = await new MonthlyOutgoingRepository(db.Context).ListForMonthAsync(bMonth.Id);
        outgoings.Should().OnlyContain(o => !o.Name.Contains("_A"));

        var txs = await new TransactionRepository(db.Context).ListForMonthAsync(bMonth.Id);
        txs.Should().OnlyContain(t => !t.Payee.Contains("_A"));
    }

    private static async Task SeedProfileAsync(TestDb db, string suffix)
    {
        db.Context.Categories.Add(new Category { Name = $"Food_{suffix}", Kind = CategoryKind.Spend });
        db.Context.RecurringOutgoings.Add(new RecurringOutgoing { Name = $"Rent_{suffix}", DefaultAmount = -1000m });

        var month = new Month
        {
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 4, 30),
            LastPayday = new DateOnly(2026, 3, 25),
            NextPayday = new DateOnly(2026, 4, 25),
            Notes = $"_{suffix}",
        };
        db.Context.Months.Add(month);
        await db.Context.SaveChangesAsync();

        db.Context.MonthlyOutgoings.Add(new MonthlyOutgoing
        {
            MonthId = month.Id, Name = $"Rent_{suffix}", Amount = -1000m,
        });
        db.Context.Transactions.Add(new Transaction
        {
            MonthId = month.Id, Date = new DateOnly(2026, 4, 5),
            Payee = $"Tesco_{suffix}", Amount = -20m,
        });
        await db.Context.SaveChangesAsync();
    }
}
