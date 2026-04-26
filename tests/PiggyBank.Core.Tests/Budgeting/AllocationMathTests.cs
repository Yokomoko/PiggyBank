using FluentAssertions;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Tests.Budgeting;

public sealed class AllocationMathTests
{
    // ----- Empty inputs -----

    [Fact]
    public void Compute_with_no_outgoings_or_transactions_returns_zero_totals()
    {
        var result = AllocationMath.Compute(Array.Empty<MonthlyOutgoing>(), Array.Empty<Transaction>());

        result.TotalOutgoings.Should().Be(0m);
        result.MonthlySpendTotal.Should().Be(0m);
        result.Details.Should().BeEmpty();
    }

    [Fact]
    public void Compute_with_only_transactions_and_no_allocations_treats_all_spend_as_general()
    {
        var monthId = Guid.NewGuid();
        var txs = new[]
        {
            Tx(monthId, -25m, categoryId: Guid.NewGuid()),
            Tx(monthId, -10m, categoryId: null),
        };

        var result = AllocationMath.Compute(Array.Empty<MonthlyOutgoing>(), txs);

        result.TotalOutgoings.Should().Be(0m);
        result.MonthlySpendTotal.Should().Be(-35m);
    }

    // ----- Non-allocation outgoings -----

    [Fact]
    public void NonAllocation_outgoing_uses_planned_amount_with_no_drawdown_state()
    {
        var monthId = Guid.NewGuid();
        var rent = Outgoing(monthId, name: "Rent", amount: -500m, isAllocation: false, categoryId: Guid.NewGuid());

        var result = AllocationMath.Compute(new[] { rent }, Array.Empty<Transaction>());

        result.TotalOutgoings.Should().Be(-500m);
        var d = result.Details.Single();
        d.OutgoingId.Should().Be(rent.Id);
        d.Allocated.Should().Be(0m);
        d.Used.Should().Be(0m);
        d.Remaining.Should().Be(0m);
        d.IsOverspent.Should().BeFalse();
        d.EffectiveAmount.Should().Be(-500m);
    }

    [Fact]
    public void NonAllocation_outgoing_with_matching_category_does_not_drawdown()
    {
        // Even if there's a transaction tagged with this outgoing's category,
        // because the outgoing is NOT flagged IsAllocation it remains in the
        // general spend pool.
        var monthId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var rent = Outgoing(monthId, name: "Rent", amount: -500m, isAllocation: false, categoryId: catId);
        var tx = Tx(monthId, -50m, categoryId: catId);

        var result = AllocationMath.Compute(new[] { rent }, new[] { tx });

        result.TotalOutgoings.Should().Be(-500m);
        result.MonthlySpendTotal.Should().Be(-50m);
        result.Details.Single().IsOverspent.Should().BeFalse();
    }

    [Fact]
    public void Wage_row_with_positive_amount_adds_to_total()
    {
        var monthId = Guid.NewGuid();
        var wage = Outgoing(monthId, name: "Salary", amount: 2000m, isAllocation: false, isWage: true);

        var result = AllocationMath.Compute(new[] { wage }, Array.Empty<Transaction>());

        result.TotalOutgoings.Should().Be(2000m);
    }

    // ----- Allocation: Used < Allocated -----

    [Fact]
    public void Allocation_under_used_keeps_planned_amount_and_reports_remaining()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var txs = new[]
        {
            Tx(monthId, -40m, categoryId: fuelCat),
            Tx(monthId, -30m, categoryId: fuelCat),
        };

        var result = AllocationMath.Compute(new[] { fuel }, txs);

        result.TotalOutgoings.Should().Be(-150m);    // planned, not −Used
        result.MonthlySpendTotal.Should().Be(0m);    // drawdowns excluded
        var d = result.Details.Single();
        d.Allocated.Should().Be(150m);
        d.Used.Should().Be(70m);
        d.Remaining.Should().Be(80m);
        d.IsOverspent.Should().BeFalse();
        d.EffectiveAmount.Should().Be(-150m);
    }

    // ----- Allocation: Used == Allocated -----

    [Fact]
    public void Allocation_exactly_used_keeps_planned_amount_and_reports_zero_remaining()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var txs = new[]
        {
            Tx(monthId, -150m, categoryId: fuelCat),
        };

        var result = AllocationMath.Compute(new[] { fuel }, txs);

        result.TotalOutgoings.Should().Be(-150m);
        result.MonthlySpendTotal.Should().Be(0m);
        var d = result.Details.Single();
        d.Used.Should().Be(150m);
        d.Remaining.Should().Be(0m);
        d.IsOverspent.Should().BeFalse();
        d.EffectiveAmount.Should().Be(-150m);
    }

    // ----- Allocation: Used > Allocated -----

    [Fact]
    public void Allocation_overspent_raises_effective_outgoing_to_minus_used()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var txs = new[]
        {
            Tx(monthId, -100m, categoryId: fuelCat),
            Tx(monthId, -90m, categoryId: fuelCat),  // total used 190 > 150
        };

        var result = AllocationMath.Compute(new[] { fuel }, txs);

        // Effective outgoing = −Used (more negative bites bigger out of the month).
        result.TotalOutgoings.Should().Be(-190m);
        // Drawdowns still don't count toward general spend total.
        result.MonthlySpendTotal.Should().Be(0m);
        var d = result.Details.Single();
        d.Allocated.Should().Be(150m);
        d.Used.Should().Be(190m);
        d.Remaining.Should().Be(-40m);  // negative — overspent by 40
        d.IsOverspent.Should().BeTrue();
        d.EffectiveAmount.Should().Be(-190m);
    }

    [Fact]
    public void Allocation_overspent_by_a_penny_flips_to_overspent()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var tx = Tx(monthId, -150.01m, categoryId: fuelCat);

        var result = AllocationMath.Compute(new[] { fuel }, new[] { tx });

        var d = result.Details.Single();
        d.IsOverspent.Should().BeTrue();
        d.EffectiveAmount.Should().Be(-150.01m);
        result.TotalOutgoings.Should().Be(-150.01m);
    }

    // ----- Drawdown isolation -----

    [Fact]
    public void Drawdown_transactions_are_excluded_from_MonthlySpendTotal()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var groceriesCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var txs = new[]
        {
            Tx(monthId, -40m, categoryId: fuelCat),       // drawdown — excluded
            Tx(monthId, -25m, categoryId: groceriesCat),  // general spend
            Tx(monthId, -10m, categoryId: null),          // uncategorised general spend
        };

        var result = AllocationMath.Compute(new[] { fuel }, txs);

        result.MonthlySpendTotal.Should().Be(-35m);  // 25 + 10, fuel drawdown excluded
    }

    [Fact]
    public void Allocation_without_CategoryId_is_treated_as_plain_outgoing()
    {
        // Edge case: IsAllocation flag is set but CategoryId is null. Without
        // a category we have nothing to drawdown — fall back to plain outgoing
        // behaviour and don't crash.
        var monthId = Guid.NewGuid();
        var stranded = Outgoing(monthId, name: "Stranded", amount: -50m, isAllocation: true, categoryId: null);
        var tx = Tx(monthId, -10m, categoryId: null);

        var result = AllocationMath.Compute(new[] { stranded }, new[] { tx });

        result.TotalOutgoings.Should().Be(-50m);
        result.MonthlySpendTotal.Should().Be(-10m);  // not a drawdown — counted as spend
        var d = result.Details.Single();
        d.IsOverspent.Should().BeFalse();
        d.EffectiveAmount.Should().Be(-50m);
    }

    // ----- Multiple allocations + multiple drawdowns -----

    [Fact]
    public void Multiple_allocations_route_drawdowns_to_their_own_pools()
    {
        var monthId = Guid.NewGuid();
        var fuelCat = Guid.NewGuid();
        var groceriesCat = Guid.NewGuid();
        var fuel = Outgoing(monthId, name: "Fuel", amount: -150m, isAllocation: true, categoryId: fuelCat);
        var groceries = Outgoing(monthId, name: "Groceries", amount: -300m, isAllocation: true, categoryId: groceriesCat);
        var rent = Outgoing(monthId, name: "Rent", amount: -800m, isAllocation: false);

        var txs = new[]
        {
            // Fuel: 60 + 50 = 110 used (under 150)
            Tx(monthId, -60m, categoryId: fuelCat),
            Tx(monthId, -50m, categoryId: fuelCat),
            // Groceries: 200 + 150 = 350 used (over 300 by 50)
            Tx(monthId, -200m, categoryId: groceriesCat),
            Tx(monthId, -150m, categoryId: groceriesCat),
            // General spend
            Tx(monthId, -20m, categoryId: null),
        };

        var result = AllocationMath.Compute(new[] { fuel, groceries, rent }, txs);

        // Total = fuel planned (-150) + groceries effective (-350) + rent (-800)
        result.TotalOutgoings.Should().Be(-1300m);
        result.MonthlySpendTotal.Should().Be(-20m);

        result.Details.Should().HaveCount(3);
        var fuelDetail = result.Details.Single(d => d.OutgoingId == fuel.Id);
        fuelDetail.Used.Should().Be(110m);
        fuelDetail.Remaining.Should().Be(40m);
        fuelDetail.IsOverspent.Should().BeFalse();
        fuelDetail.EffectiveAmount.Should().Be(-150m);

        var groceriesDetail = result.Details.Single(d => d.OutgoingId == groceries.Id);
        groceriesDetail.Used.Should().Be(350m);
        groceriesDetail.Remaining.Should().Be(-50m);
        groceriesDetail.IsOverspent.Should().BeTrue();
        groceriesDetail.EffectiveAmount.Should().Be(-350m);

        var rentDetail = result.Details.Single(d => d.OutgoingId == rent.Id);
        rentDetail.Allocated.Should().Be(0m);
        rentDetail.Used.Should().Be(0m);
        rentDetail.EffectiveAmount.Should().Be(-800m);
    }

    [Fact]
    public void Details_preserve_input_order()
    {
        var monthId = Guid.NewGuid();
        var a = Outgoing(monthId, name: "A", amount: -10m);
        var b = Outgoing(monthId, name: "B", amount: -20m);
        var c = Outgoing(monthId, name: "C", amount: -30m);

        var result = AllocationMath.Compute(new[] { a, b, c }, Array.Empty<Transaction>());

        result.Details.Select(d => d.OutgoingId).Should().Equal(a.Id, b.Id, c.Id);
    }

    // ----- Helpers -----

    private static MonthlyOutgoing Outgoing(
        Guid monthId,
        string name,
        decimal amount,
        bool isAllocation = false,
        Guid? categoryId = null,
        bool isWage = false)
        => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            MonthId = monthId,
            Name = name,
            Amount = amount,
            IsAllocation = isAllocation,
            CategoryId = categoryId,
            IsWage = isWage,
        };

    private static Transaction Tx(Guid monthId, decimal amount, Guid? categoryId)
        => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            MonthId = monthId,
            Date = new DateOnly(2026, 4, 24),
            Payee = "test",
            Amount = amount,
            CategoryId = categoryId,
        };
}
