using FluentAssertions;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Services;

public sealed class MonthServiceTests
{
    [Fact]
    public async Task Create_snapshots_active_recurring_outgoings_into_monthly_outgoings()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(profile.Id);
        var recurring = new RecurringOutgoingRepository(db.Context);
        await recurring.AddAsync(new RecurringOutgoing { Name = "Mortgage", DefaultAmount = -1000m, SortOrder = 0 });
        await recurring.AddAsync(new RecurringOutgoing { Name = "Broadband", DefaultAmount = -48m, SortOrder = 1 });
        await recurring.AddAsync(new RecurringOutgoing { Name = "Wage", DefaultAmount = 3000m, IsIncome = true, IsWage = true, SortOrder = 99 });
        await recurring.AddAsync(new RecurringOutgoing { Name = "Old gym", DefaultAmount = -40m, IsArchived = true });

        var svc = BuildService(db);
        var month = await svc.CreateAsync(
            lastPayday: new DateOnly(2026, 3, 25),
            nextPayday: new DateOnly(2026, 4, 25));

        var snapshots = await new MonthlyOutgoingRepository(db.Context).ListForMonthAsync(month.Id);
        snapshots.Select(s => s.Name).Should().BeEquivalentTo(new[] { "Mortgage", "Broadband", "Wage" });
        snapshots.Single(s => s.Name == "Wage").IsWage.Should().BeTrue();
    }

    [Fact]
    public async Task Create_defaults_carryover_to_zero()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);

        var svc = BuildService(db);
        var month = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));

        month.CarriedOverBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Create_does_not_auto_rollover_even_from_closed_prior()
    {
        // Rule: rollover is manual. Prior closed month's balance should
        // NOT silently flow into the new month even if it's available.
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var march = await svc.CreateAsync(new DateOnly(2026, 2, 25), new DateOnly(2026, 3, 25));
        var outgoings = new MonthlyOutgoingRepository(db.Context);
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Wage", Amount = 3000m });
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Rent", Amount = -1000m });
        await svc.CloseAsync(march.Id);

        var april = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));

        april.CarriedOverBalance.Should().Be(0m,
            "rollover must be manual — the user will apply it explicitly after confirming the prior month is final");
    }

    [Fact]
    public async Task Create_honours_carryover_override()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var april = await svc.CreateAsync(
            new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25),
            carryOverOverride: 500m);

        april.CarriedOverBalance.Should().Be(500m);
    }

    [Fact]
    public async Task SuggestRollover_returns_null_when_no_prior_month()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var only = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));
        (await svc.SuggestRolloverAsync(only.Id)).Should().BeNull();
    }

    [Fact]
    public async Task SuggestRollover_returns_null_when_prior_still_open()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var march = await svc.CreateAsync(new DateOnly(2026, 2, 25), new DateOnly(2026, 3, 25));
        await new MonthlyOutgoingRepository(db.Context).AddAsync(
            new MonthlyOutgoing { MonthId = march.Id, Name = "Wage", Amount = 3000m });
        // don't close march

        var april = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));
        (await svc.SuggestRolloverAsync(april.Id)).Should().BeNull();
    }

    [Fact]
    public async Task SuggestRollover_returns_closing_balance_when_prior_closed()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var march = await svc.CreateAsync(new DateOnly(2026, 2, 25), new DateOnly(2026, 3, 25));
        var outgoings = new MonthlyOutgoingRepository(db.Context);
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Wage", Amount = 3000m });
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Rent", Amount = -1000m });
        await svc.CloseAsync(march.Id);

        var april = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));
        var suggestion = await svc.SuggestRolloverAsync(april.Id);
        suggestion.Should().Be(2000m);

        // But note — the april record is unchanged until applied.
        var aprilFresh = await new MonthRepository(db.Context).FindAsync(april.Id);
        aprilFresh!.CarriedOverBalance.Should().Be(0m);
    }

    [Fact]
    public async Task ApplyRollover_updates_carry_over_when_prior_closed()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var march = await svc.CreateAsync(new DateOnly(2026, 2, 25), new DateOnly(2026, 3, 25));
        var outgoings = new MonthlyOutgoingRepository(db.Context);
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Wage", Amount = 3000m });
        await outgoings.AddAsync(new MonthlyOutgoing { MonthId = march.Id, Name = "Rent", Amount = -1000m });
        await svc.CloseAsync(march.Id);

        var april = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));
        var applied = await svc.ApplyRolloverFromPriorAsync(april.Id);
        applied.Should().Be(2000m);

        var aprilFresh = await new MonthRepository(db.Context).FindAsync(april.Id);
        aprilFresh!.CarriedOverBalance.Should().Be(2000m);
    }

    [Fact]
    public async Task ApplyRollover_throws_when_prior_open()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        await svc.CreateAsync(new DateOnly(2026, 2, 25), new DateOnly(2026, 3, 25));
        var april = await svc.CreateAsync(new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 25));

        var act = async () => await svc.ApplyRolloverFromPriorAsync(april.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*still open*");
    }

    [Fact]
    public async Task Create_rejects_non_positive_pay_window()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        var svc = BuildService(db);

        var act = async () => await svc.CreateAsync(
            new DateOnly(2026, 4, 25), new DateOnly(2026, 4, 25));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static MonthService BuildService(TestDb db)
        => new(
            db.Context,
            new MonthRepository(db.Context),
            new MonthlyOutgoingRepository(db.Context),
            new RecurringOutgoingRepository(db.Context),
            new TransactionRepository(db.Context));
}
