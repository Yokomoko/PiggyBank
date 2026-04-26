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
