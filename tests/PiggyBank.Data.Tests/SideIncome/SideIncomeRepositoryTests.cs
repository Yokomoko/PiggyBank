using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.SideIncome;

public sealed class SideIncomeRepositoryTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Add_and_list_round_trips_the_entry()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new SideIncomeRepository(db.Context, Clock);

        await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 15),
            Description = "Babysitting",
            DurationHours = 4m,
            HourlyRate = 12.50m,
            Total = 50m,
        });

        var list = await repo.ListEntriesAsync();
        list.Should().HaveCount(1);
        list[0].Description.Should().Be("Babysitting");
        list[0].Total.Should().Be(50m);
        // CreatedAtUtc gets stamped if not pre-set.
        list[0].CreatedAtUtc.Should().Be(new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task List_orders_newest_PaidOn_first()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new SideIncomeRepository(db.Context, Clock);

        await repo.AddEntryAsync(Entry(new(2026, 1, 10), 100m, "Jan"));
        await repo.AddEntryAsync(Entry(new(2026, 3, 15), 200m, "Mar"));
        await repo.AddEntryAsync(Entry(new(2026, 2, 5), 150m, "Feb"));

        var list = await repo.ListEntriesAsync();
        list.Select(e => e.Description).Should().ContainInOrder("Mar", "Feb", "Jan");
    }

    [Fact]
    public async Task Delete_entry_cascades_to_allocations()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new SideIncomeRepository(db.Context, Clock);

        var entry = await repo.AddEntryAsync(Entry(new(2026, 3, 15), 100m, "Job"));
        await repo.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = entry.Id,
            Amount = 40m,
            Target = SideIncomeAllocationTarget.Pocket,
        });

        await repo.DeleteEntryAsync(entry.Id);

        (await repo.ListEntriesAsync()).Should().BeEmpty();
        (await repo.ListAllocationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task List_allocations_for_entry_filters_correctly()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var repo = new SideIncomeRepository(db.Context, Clock);

        var e1 = await repo.AddEntryAsync(Entry(new(2026, 3, 1), 100m, "E1"));
        var e2 = await repo.AddEntryAsync(Entry(new(2026, 3, 2), 100m, "E2"));
        await repo.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = e1.Id, Amount = 30m, Target = SideIncomeAllocationTarget.Pocket,
        });
        await repo.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = e1.Id, Amount = 20m, Target = SideIncomeAllocationTarget.MainLedger,
        });
        await repo.AddAllocationAsync(new SideIncomeAllocation
        {
            SideIncomeEntryId = e2.Id, Amount = 50m, Target = SideIncomeAllocationTarget.Pocket,
        });

        var forE1 = await repo.ListAllocationsForEntryAsync(e1.Id);
        forE1.Should().HaveCount(2);
        forE1.Sum(a => a.Amount).Should().Be(50m);
    }

    private static SideIncomeEntry Entry(DateOnly paidOn, decimal total, string description) => new()
    {
        PaidOn = paidOn,
        Total = total,
        Description = description,
    };

    private static async Task<Profile> SeedProfile(TestDb db)
    {
        var profile = new Profile { DisplayName = "Alice" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        return profile;
    }
}
