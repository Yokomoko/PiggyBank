using FluentAssertions;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Tests.Budgeting;

public class SideIncomeMathTests
{
    [Fact]
    public void SuggestedTotal_multiplies_duration_by_rate()
        => SideIncomeMath.SuggestedTotal(3.5m, 15m).Should().Be(52.50m);

    [Fact]
    public void SuggestedTotal_null_when_duration_missing()
        => SideIncomeMath.SuggestedTotal(null, 15m).Should().BeNull();

    [Fact]
    public void SuggestedTotal_null_when_rate_missing()
        => SideIncomeMath.SuggestedTotal(3m, null).Should().BeNull();

    [Fact]
    public void SuggestedTotal_rounds_half_up_to_two_dp()
        => SideIncomeMath.SuggestedTotal(3.333m, 10m).Should().Be(33.33m);

    [Fact]
    public void Remaining_is_total_minus_allocations_for_this_entry_only()
    {
        var entry = MakeEntry(total: 100m);
        var other = MakeEntry(total: 50m);
        var allocations = new[]
        {
            Alloc(entry.Id, 30m),
            Alloc(entry.Id, 20m),
            Alloc(other.Id, 50m), // different entry — should not affect `entry`'s remaining
        };

        SideIncomeMath.RemainingFor(entry, allocations).Should().Be(50m);
    }

    [Fact]
    public void Remaining_clamps_to_zero_when_over_allocated()
    {
        var entry = MakeEntry(total: 100m);
        var allocations = new[]
        {
            Alloc(entry.Id, 80m),
            Alloc(entry.Id, 40m), // would take remaining to -20
        };

        SideIncomeMath.RemainingFor(entry, allocations).Should().Be(0m);
    }

    [Fact]
    public void GroupByCalendarMonth_bundles_entries_by_PaidOn_month_and_computes_totals()
    {
        var janA = MakeEntry(total: 100m, paidOn: new(2026, 1, 5));
        var janB = MakeEntry(total: 50m, paidOn: new(2026, 1, 20));
        var feb = MakeEntry(total: 200m, paidOn: new(2026, 2, 14));
        var allocs = new[]
        {
            Alloc(janA.Id, 30m),   // partial
            Alloc(feb.Id, 200m),   // full
        };

        var groups = SideIncomeMath.GroupByCalendarMonth(new[] { janA, janB, feb }, allocs);

        groups.Should().HaveCount(2);
        groups[0].PeriodStart.Should().Be(new DateOnly(2026, 2, 1));   // newest first
        groups[0].TotalEarned.Should().Be(200m);
        groups[0].TotalAllocated.Should().Be(200m);
        groups[0].Remaining.Should().Be(0m);

        groups[1].PeriodStart.Should().Be(new DateOnly(2026, 1, 1));
        groups[1].TotalEarned.Should().Be(150m);
        groups[1].TotalAllocated.Should().Be(30m);
        groups[1].Remaining.Should().Be(120m);
        groups[1].Entries.Should().HaveCount(2);
    }

    private static SideIncomeEntry MakeEntry(decimal total, DateOnly? paidOn = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            PaidOn = paidOn ?? new DateOnly(2026, 4, 24),
            Total = total,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static SideIncomeAllocation Alloc(Guid entryId, decimal amount)
        => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            SideIncomeEntryId = entryId,
            Amount = amount,
            Target = SideIncomeAllocationTarget.Pocket,
        };
}
