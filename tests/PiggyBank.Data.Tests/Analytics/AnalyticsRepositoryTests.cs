using FluentAssertions;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Analytics;

public sealed class AnalyticsRepositoryTests
{
    [Fact]
    public async Task MonthlySpend_returns_positive_magnitudes_ordered_oldest_to_newest()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);

        var m1 = new Month { PeriodStart = new(2026, 1, 25), PeriodEnd = new(2026, 2, 24), LastPayday = new(2026, 1, 25), NextPayday = new(2026, 2, 25) };
        var m2 = new Month { PeriodStart = new(2026, 2, 25), PeriodEnd = new(2026, 3, 24), LastPayday = new(2026, 2, 25), NextPayday = new(2026, 3, 25) };
        db.Context.Months.AddRange(m1, m2);
        await db.Context.SaveChangesAsync();

        // Spend: m1 = -120, m2 = -80 (plus a future and an income — both ignored)
        db.Context.Transactions.AddRange(
            new Transaction { MonthId = m1.Id, Date = new(2026, 2, 3), Payee = "Tesco", Amount = -50m },
            new Transaction { MonthId = m1.Id, Date = new(2026, 2, 12), Payee = "Petrol", Amount = -70m },
            new Transaction { MonthId = m2.Id, Date = new(2026, 3, 4), Payee = "Tesco", Amount = -80m },
            new Transaction { MonthId = m2.Id, Date = new(2026, 3, 6), Payee = "Refund", Amount = 30m },      // income — ignored
            new Transaction { MonthId = m2.Id, Date = new(2026, 3, 10), Payee = "Future", Amount = -99m }
        );
        await db.Context.SaveChangesAsync();

        var repo = new AnalyticsRepository(db.Context);
        var result = await repo.GetMonthlySpendAsync();

        result.Should().HaveCount(2);
        result[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 25));
        result[0].Total.Should().Be(120m);
        result[1].PeriodStart.Should().Be(new DateOnly(2026, 2, 25));
        // 80 (Tesco) + 99 (was the "Future" tx — IsFuture filter removed
        // when the column was dropped, so all spend now counts).
        result[1].Total.Should().Be(179m);
    }

    [Fact]
    public async Task CategorySpend_excludes_income_and_orders_by_total_desc()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);

        var food = new Category { Name = "Food", Kind = CategoryKind.Spend };
        var petrol = new Category { Name = "Petrol", Kind = CategoryKind.Spend };
        db.Context.Categories.AddRange(food, petrol);

        var month = new Month { PeriodStart = new(2026, 1, 25), PeriodEnd = new(2026, 2, 24), LastPayday = new(2026, 1, 25), NextPayday = new(2026, 2, 25) };
        db.Context.Months.Add(month);
        await db.Context.SaveChangesAsync();

        db.Context.Transactions.AddRange(
            new Transaction { MonthId = month.Id, Date = new(2026, 2, 3), Payee = "Tesco", Amount = -50m, CategoryId = food.Id },
            new Transaction { MonthId = month.Id, Date = new(2026, 2, 4), Payee = "Aldi",  Amount = -30m, CategoryId = food.Id },
            new Transaction { MonthId = month.Id, Date = new(2026, 2, 5), Payee = "BP",    Amount = -60m, CategoryId = petrol.Id },
            new Transaction { MonthId = month.Id, Date = new(2026, 2, 6), Payee = "Refund",Amount =  20m, CategoryId = food.Id }  // income — excluded
        );
        await db.Context.SaveChangesAsync();

        var repo = new AnalyticsRepository(db.Context);
        var result = await repo.GetSpendByCategoryAsync(new(2026, 2, 1), new(2026, 2, 28));

        result.Should().HaveCount(2);
        result[0].CategoryName.Should().Be("Food");   // £80 > £60
        result[0].Total.Should().Be(80m);
        result[1].CategoryName.Should().Be("Petrol");
        result[1].Total.Should().Be(60m);
    }
}
