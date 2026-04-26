using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.SideIncome;

public sealed class SideIncomeServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Allocate_to_pocket_bumps_balance_creates_deposit_and_records_allocation()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, _) = Build(db);

        var pocket = await new PocketRepository(db.Context, Clock).AddAsync(new Pocket
        {
            Name = "Emergency",
            CurrentBalance = 50m,
            AutoSavePercent = 0m,
        });
        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 15),
            Total = 100m,
            Description = "Cleaning gig",
        });

        await svc.AllocateToPocketAsync(entry.Id, pocket.Id, 40m);

        // Pocket balance bumped by allocation amount.
        var pockets = new PocketRepository(db.Context, Clock);
        (await pockets.FindAsync(pocket.Id))!.CurrentBalance.Should().Be(90m);

        // Side-income allocation references the created Deposit.
        var allocs = await repo.ListAllocationsForEntryAsync(entry.Id);
        allocs.Should().HaveCount(1);
        allocs[0].Target.Should().Be(SideIncomeAllocationTarget.Pocket);
        allocs[0].PocketId.Should().Be(pocket.Id);
        allocs[0].PocketDepositId.Should().NotBeNull();
        allocs[0].Amount.Should().Be(40m);
    }

    [Fact]
    public async Task Allocate_to_main_ledger_creates_positive_transaction_in_containing_month()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, months) = Build(db);

        // Payday month: 24 Mar – 23 Apr. Side income PaidOn = 27 Mar falls inside.
        var month = await months.AddAsync(new Month
        {
            PeriodStart = new(2026, 3, 24),
            PeriodEnd = new(2026, 4, 23),
            LastPayday = new(2026, 3, 24),
            NextPayday = new(2026, 4, 24),
        });

        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 27),
            Total = 80m,
            Description = "Babysitting",
        });

        await svc.AllocateToMainLedgerAsync(entry.Id, 60m);

        var txRepo = new TransactionRepository(db.Context);
        var txs = await txRepo.ListForMonthAsync(month.Id);
        txs.Should().HaveCount(1);
        txs[0].Amount.Should().Be(60m);          // positive = income
        txs[0].Payee.Should().Be("Babysitting");
        txs[0].Date.Should().Be(new DateOnly(2026, 3, 27));

        var allocs = await repo.ListAllocationsForEntryAsync(entry.Id);
        allocs.Single().Target.Should().Be(SideIncomeAllocationTarget.MainLedger);
        allocs.Single().MonthId.Should().Be(month.Id);
        allocs.Single().LedgerTransactionId.Should().Be(txs[0].Id);
    }

    [Fact]
    public async Task Allocate_to_main_ledger_rejects_when_no_month_contains_PaidOn()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, _) = Build(db);

        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 15),
            Total = 50m,
        });

        var act = () => svc.AllocateToMainLedgerAsync(entry.Id, 50m);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No open payday month*");
    }

    [Fact]
    public async Task Allocate_to_main_ledger_rejects_closed_month()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, months) = Build(db);

        var month = await months.AddAsync(new Month
        {
            PeriodStart = new(2026, 3, 24),
            PeriodEnd = new(2026, 4, 23),
            LastPayday = new(2026, 3, 24),
            NextPayday = new(2026, 4, 24),
            IsClosed = true,
        });

        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 27),
            Total = 40m,
        });

        var act = () => svc.AllocateToMainLedgerAsync(entry.Id, 40m);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    [Fact]
    public async Task Partial_allocations_track_remaining_across_both_targets()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, months) = Build(db);

        var pocket = await new PocketRepository(db.Context, Clock).AddAsync(new Pocket
        {
            Name = "Holiday", CurrentBalance = 0m, AutoSavePercent = 0m,
        });
        var month = await months.AddAsync(new Month
        {
            PeriodStart = new(2026, 3, 24),
            PeriodEnd = new(2026, 4, 23),
            LastPayday = new(2026, 3, 24),
            NextPayday = new(2026, 4, 24),
        });
        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 27),
            Total = 100m,
        });

        await svc.AllocateToPocketAsync(entry.Id, pocket.Id, 30m);
        await svc.AllocateToMainLedgerAsync(entry.Id, 50m);

        var allocations = await repo.ListAllocationsForEntryAsync(entry.Id);
        allocations.Sum(a => a.Amount).Should().Be(80m);

        // Remaining is 20m. Can't over-allocate.
        var overAllocate = () => svc.AllocateToPocketAsync(entry.Id, pocket.Id, 30m);
        await overAllocate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*only £20.00 remains*");
    }

    [Fact]
    public async Task Allocate_rejects_non_positive_amount()
    {
        using var db = TestDb.CreateAdmin();
        await SeedProfile(db);
        var (repo, svc, _) = Build(db);

        var pocket = await new PocketRepository(db.Context, Clock).AddAsync(new Pocket
        {
            Name = "Christmas", CurrentBalance = 0m, AutoSavePercent = 0m,
        });
        var entry = await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 27), Total = 100m,
        });

        var act = () => svc.AllocateToPocketAsync(entry.Id, pocket.Id, 0m);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("amount");
    }

    private static async Task<Profile> SeedProfile(TestDb db)
    {
        var profile = new Profile { DisplayName = "Alice" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();
        db.SwitchToProfile(profile.Id);
        return profile;
    }

    private static (
        SideIncomeRepository repo,
        SideIncomeService svc,
        MonthRepository months
    ) Build(TestDb db)
    {
        var repo = new SideIncomeRepository(db.Context, Clock);
        var months = new MonthRepository(db.Context);
        var txRepo = new TransactionRepository(db.Context);
        var pocketRepo = new PocketRepository(db.Context, Clock);
        var depositRepo = new DepositRepository(db.Context);
        var depositSvc = new DepositService(db.Context, pocketRepo, depositRepo, Clock);
        var svc = new SideIncomeService(db.Context, repo, months, txRepo, depositSvc);
        return (repo, svc, months);
    }
}
