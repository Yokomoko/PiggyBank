using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Debts;

public sealed class DebtRepositoryTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Add_and_list_round_trips_the_debt()
    {
        using var db = TestDb.CreateAdmin();
        var profile = await SeedProfile(db);

        var repo = new DebtRepository(db.Context, Clock);
        await repo.AddAsync(new Debt
        {
            Name = "Halifax CC",
            Kind = DebtKind.CreditCard,
            Limit = 4250m,
            AnnualPercentageRate = 0.199m,
            OpeningBalance = -1200m,
        });

        var list = await repo.ListAsync();
        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Halifax CC");
        list[0].Limit.Should().Be(4250m);
    }

    [Fact]
    public async Task Archive_hides_debt_from_default_list()
    {
        using var db = TestDb.CreateAdmin();
        _ = await SeedProfile(db);
        var repo = new DebtRepository(db.Context, Clock);

        var alive = await repo.AddAsync(new Debt
        {
            Name = "Alive", Kind = DebtKind.Loan, OpeningBalance = -500m,
        });
        var gone = await repo.AddAsync(new Debt
        {
            Name = "Paid off", Kind = DebtKind.CreditCard, OpeningBalance = -200m,
        });
        await repo.ArchiveAsync(gone.Id);

        var defaultList = await repo.ListAsync();
        defaultList.Select(d => d.Name).Should().BeEquivalentTo(new[] { "Alive" });

        var withArchived = await repo.ListAsync(includeArchived: true);
        withArchived.Should().HaveCount(2);
        withArchived.Single(d => d.Name == "Paid off").ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Latest_balance_uses_most_recent_snapshot()
    {
        using var db = TestDb.CreateAdmin();
        _ = await SeedProfile(db);
        var repo = new DebtRepository(db.Context, Clock);

        var debt = await repo.AddAsync(new Debt
        {
            Name = "Car Loan", Kind = DebtKind.Finance, OpeningBalance = -9000m,
        });

        await repo.AddSnapshotAsync(new DebtSnapshot
        {
            DebtId = debt.Id, SnapshotDate = new(2026, 2, 28), Balance = -8500m,
        });
        await repo.AddSnapshotAsync(new DebtSnapshot
        {
            DebtId = debt.Id, SnapshotDate = new(2026, 3, 31), Balance = -8000m,
        });

        var summary = await repo.ListWithLatestBalancesAsync();
        summary.Should().HaveCount(1);
        summary[0].LatestBalance.Should().Be(-8000m);
        summary[0].LatestSnapshotDate.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task Latest_balance_falls_back_to_opening_when_no_snapshots()
    {
        using var db = TestDb.CreateAdmin();
        _ = await SeedProfile(db);
        var repo = new DebtRepository(db.Context, Clock);

        await repo.AddAsync(new Debt
        {
            Name = "Phone", Kind = DebtKind.Finance, OpeningBalance = -400m,
        });

        var summary = await repo.ListWithLatestBalancesAsync();
        summary[0].LatestBalance.Should().Be(-400m);
        summary[0].LatestSnapshotDate.Should().BeNull();
    }

    [Fact]
    public async Task AddSnapshot_stamps_RecordedAtUtc_from_injected_clock()
    {
        using var db = TestDb.CreateAdmin();
        _ = await SeedProfile(db);
        var repo = new DebtRepository(db.Context, Clock);

        var debt = await repo.AddAsync(new Debt
        {
            Name = "Sainsburys", Kind = DebtKind.CreditCard, OpeningBalance = -471m,
        });

        var snap = await repo.AddSnapshotAsync(new DebtSnapshot
        {
            DebtId = debt.Id, SnapshotDate = new(2026, 4, 24), Balance = -400m,
        });

        snap.RecordedAtUtc.Should().Be(new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc));
    }

    private static async Task<Profile> SeedProfile(TestDb db)
    {
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        return profile;
    }
}
