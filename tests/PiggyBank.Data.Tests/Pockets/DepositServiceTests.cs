using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Pockets;

public sealed class DepositServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RecordAsync_distributes_by_autosave_percent()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        var holiday = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Holiday", CurrentBalance = 100m, AutoSavePercent = 30m,
        });
        var mort = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Mortgage Overpayment", CurrentBalance = 0m, AutoSavePercent = 15m,
        });

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 200m, notes: "April");

        result.Allocations.Should().HaveCount(2);
        result.Allocations.Single(a => a.PocketId == holiday.Id).Amount.Should().Be(60m);
        result.Allocations.Single(a => a.PocketId == mort.Id).Amount.Should().Be(30m);
        result.Unallocated.Should().Be(110m);

        var reloaded = await pocketRepo.ListAsync();
        reloaded.Single(p => p.Id == holiday.Id).CurrentBalance.Should().Be(160m);
        reloaded.Single(p => p.Id == mort.Id).CurrentBalance.Should().Be(30m);

        result.Deposit.Notes.Should().Be("April");
        result.Deposit.RecordedAtUtc.Should().Be(new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task RecordAsync_snapshots_autosave_percent_at_deposit_time()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        var p = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Christmas", CurrentBalance = 0m, AutoSavePercent = 5m,
        });
        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 100m);

        p.AutoSavePercent = 50m;
        await pocketRepo.UpdateAsync(p);

        var allocs = await depositRepo.ListAllocationsAsync(result.Deposit.Id);
        allocs.Single().AutoSavePercentAtDeposit.Should().Be(5m);
    }

    [Fact]
    public async Task RecordAsync_skips_archived_pockets()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        var active = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Active", CurrentBalance = 0m, AutoSavePercent = 10m,
        });
        var archived = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Archived", CurrentBalance = 0m, AutoSavePercent = 80m,
        });
        await pocketRepo.ArchiveAsync(archived.Id);

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 500m);

        result.Allocations.Should().HaveCount(1);
        result.Allocations[0].PocketId.Should().Be(active.Id);
        result.Allocations[0].Amount.Should().Be(50m);
    }

    [Fact]
    public async Task RecordAsync_ignores_pockets_with_zero_autosave()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        await pocketRepo.AddAsync(new Pocket
        {
            Name = "Primary", CurrentBalance = 0m, AutoSavePercent = 0m,
        });
        var savings = await pocketRepo.AddAsync(new Pocket
        {
            Name = "Savings", CurrentBalance = 0m, AutoSavePercent = 10m,
        });

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 1_000m);

        result.Allocations.Should().HaveCount(1);
        result.Allocations[0].PocketId.Should().Be(savings.Id);
        result.Unallocated.Should().Be(900m);
    }

    [Fact]
    public async Task RecordAsync_reports_zero_unallocated_when_autosave_sums_to_hundred()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        await pocketRepo.AddAsync(new Pocket { Name = "A", CurrentBalance = 0m, AutoSavePercent = 50m });
        await pocketRepo.AddAsync(new Pocket { Name = "B", CurrentBalance = 0m, AutoSavePercent = 50m });

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 100m);
        result.Unallocated.Should().Be(0m);
    }

    /// <summary>Regression for the user-reported "£63.50 in, £63.52 out"
    /// rounding drift on 2026-04-27. Naive per-pocket round-half-away
    /// rounded both 15.875 → 15.88 and 3.175 → 3.18 (×2), pushing the
    /// total 2p over the deposit. Largest-remainder apportionment must
    /// keep the sum exactly equal to the deposit even when several
    /// pockets land on the half-penny boundary.</summary>
    [Fact]
    public async Task RecordAsync_distributes_exactly_when_percentages_force_rounding()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        // 25 + 15 + 10 + 10 + 30 + 5 + 5 = 100
        await pocketRepo.AddAsync(new Pocket { Name = "Christmas",  CurrentBalance = 0m, AutoSavePercent = 25m });
        await pocketRepo.AddAsync(new Pocket { Name = "Buffer",     CurrentBalance = 0m, AutoSavePercent = 15m });
        await pocketRepo.AddAsync(new Pocket { Name = "Tech",       CurrentBalance = 0m, AutoSavePercent = 10m });
        await pocketRepo.AddAsync(new Pocket { Name = "Mortgage OP",CurrentBalance = 0m, AutoSavePercent = 10m });
        await pocketRepo.AddAsync(new Pocket { Name = "Holiday",    CurrentBalance = 0m, AutoSavePercent = 30m });
        await pocketRepo.AddAsync(new Pocket { Name = "SIPP A",     CurrentBalance = 0m, AutoSavePercent = 5m });
        await pocketRepo.AddAsync(new Pocket { Name = "SIPP B",     CurrentBalance = 0m, AutoSavePercent = 5m });

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 63.50m);

        // The cardinal property: total distributed equals the deposit.
        result.Allocations.Sum(a => a.Amount).Should().Be(63.50m);
        result.Unallocated.Should().Be(0m);

        // Every individual share is within 1p of its mathematical exact
        // proportional value — apportionment never pushes a pocket more
        // than one penny away from its fair share — AND the pocket's
        // CurrentBalance was actually persisted to that share. The
        // separate balance-persisted check guards against a regression
        // where one pocket's UpdateAsync silently no-ops while peers
        // update fine.
        var allPockets = await pocketRepo.ListAsync();
        foreach (var a in result.Allocations)
        {
            var pocket = allPockets.Single(p => p.Id == a.PocketId);
            var exact = 63.50m * (pocket.AutoSavePercent / 100m);
            Math.Abs(a.Amount - exact).Should().BeLessThanOrEqualTo(0.01m);

            // Started at 0; should now equal the allocated share.
            pocket.CurrentBalance.Should().Be(a.Amount,
                $"pocket \"{pocket.Name}\" should have its allocation persisted");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordAsync_rejects_non_positive_amount(decimal amount)
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        var act = () => svc.RecordAsync(new DateOnly(2026, 4, 24), amount);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("amount");
    }

    [Fact]
    public async Task RecordAsync_writes_no_allocations_when_no_active_autosaving_pockets()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var svc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);

        await pocketRepo.AddAsync(new Pocket { Name = "Primary", CurrentBalance = 0m, AutoSavePercent = 0m });

        var result = await svc.RecordAsync(new DateOnly(2026, 4, 24), 250m);
        result.Allocations.Should().BeEmpty();
        result.Unallocated.Should().Be(250m);

        var list = await depositRepo.ListAsync();
        list.Should().HaveCount(1);
        list[0].Amount.Should().Be(250m);
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
