using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Pockets;

public sealed class PocketRepositoryTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Add_and_list_round_trips_the_pocket()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new PocketRepository(db.Context, Clock);

        await repo.AddAsync(new Pocket
        {
            Name = "Christmas",
            CurrentBalance = 120m,
            AutoSavePercent = 5m,
            Goal = 295m,
            AnnualInterestRate = 0.0525m,
        });

        var list = await repo.ListAsync();
        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Christmas");
        list[0].AutoSavePercent.Should().Be(5m);
        list[0].Goal.Should().Be(295m);
    }

    [Fact]
    public async Task List_is_ordered_by_sort_order_then_name()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new PocketRepository(db.Context, Clock);

        await repo.AddRangeAsync(new[]
        {
            new Pocket { Name = "B", CurrentBalance = 0m, AutoSavePercent = 0m, SortOrder = 2 },
            new Pocket { Name = "A", CurrentBalance = 0m, AutoSavePercent = 0m, SortOrder = 1 },
            new Pocket { Name = "C", CurrentBalance = 0m, AutoSavePercent = 0m, SortOrder = 1 },
        });

        var list = await repo.ListAsync();
        list.Select(p => p.Name).Should().ContainInOrder("A", "C", "B");
    }

    [Fact]
    public async Task Archive_hides_pocket_from_default_list()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new PocketRepository(db.Context, Clock);

        var alive = await repo.AddAsync(new Pocket { Name = "Alive", CurrentBalance = 0m, AutoSavePercent = 0m });
        var gone = await repo.AddAsync(new Pocket { Name = "Gone", CurrentBalance = 0m, AutoSavePercent = 0m });
        await repo.ArchiveAsync(gone.Id);

        var defaultList = await repo.ListAsync();
        defaultList.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Alive" });

        var withArchived = await repo.ListAsync(includeArchived: true);
        withArchived.Should().HaveCount(2);
        withArchived.Single(p => p.Name == "Gone").ArchivedAtUtc
            .Should().Be(new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetAutoSaveSumAsync_sums_only_non_archived_pockets()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new PocketRepository(db.Context, Clock);

        await repo.AddAsync(new Pocket { Name = "A", CurrentBalance = 0m, AutoSavePercent = 30m });
        await repo.AddAsync(new Pocket { Name = "B", CurrentBalance = 0m, AutoSavePercent = 15m });
        var retired = await repo.AddAsync(new Pocket { Name = "Retired", CurrentBalance = 0m, AutoSavePercent = 50m });
        await repo.ArchiveAsync(retired.Id);

        var sum = await repo.GetAutoSaveSumAsync();
        sum.Should().Be(45m);
    }

    [Fact]
    public async Task GetAutoSaveSumAsync_returns_zero_when_no_pockets()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new PocketRepository(db.Context, Clock);

        var sum = await repo.GetAutoSaveSumAsync();
        sum.Should().Be(0m);
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
