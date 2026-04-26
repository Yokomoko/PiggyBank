using FluentAssertions;
using PiggyBank.Core.Budgeting;

namespace PiggyBank.Core.Tests.Budgeting;

public sealed class BudgetCalculatorTests
{
    private static readonly BudgetCalculator Calc = new(TimeProvider.System);

    // ----- GrandTotal -----

    [Fact]
    public void GrandTotal_basic()
        => Calc.GrandTotal(total: 1200m, monthlySpendTotal: 300m, carriedOver: 50m).Should().Be(950m);

    [Fact]
    public void GrandTotal_negative_when_overspent()
        => Calc.GrandTotal(1200m, 1400m, 0m).Should().Be(-200m);

    [Fact]
    public void GrandTotal_includes_negative_carryover()
        => Calc.GrandTotal(1000m, 0m, -50m).Should().Be(950m);

    // ----- AllowedSpendPerDay -----

    [Fact]
    public void AllowedSpendPerDay_divides_cleanly()
    {
        var i = Budget(grandTotal: 1000m, daysToNextPayday: 10m);
        Calc.AllowedSpendPerDay(i).Should().Be(100m);
    }

    [Fact]
    public void AllowedSpendPerDay_is_zero_when_zero_days()
        => Calc.AllowedSpendPerDay(Budget(grandTotal: 1000m, daysToNextPayday: 0m)).Should().Be(0m);

    [Fact]
    public void AllowedSpendPerDay_is_zero_when_negative_days()
        => Calc.AllowedSpendPerDay(Budget(grandTotal: 1000m, daysToNextPayday: -5m)).Should().Be(0m);

    [Fact]
    public void AllowedSpendPerDay_propagates_negative_grand_total()
        => Calc.AllowedSpendPerDay(Budget(grandTotal: -200m, daysToNextPayday: 10m)).Should().Be(-20m);

    // ----- AllowedSpendWholeDays -----

    [Fact]
    public void AllowedSpendWholeDays_rounds_up_fractional_day()
    {
        // 2.3 days → 3 whole days → 300/3 = 100
        var i = Budget(grandTotal: 300m, daysToNextPayday: 2.3m);
        Calc.AllowedSpendWholeDays(i).Should().Be(100m);
    }

    [Fact]
    public void AllowedSpendWholeDays_returns_zero_when_zero()
        => Calc.AllowedSpendWholeDays(Budget(grandTotal: 1000m, daysToNextPayday: 0m)).Should().Be(0m);

    // ----- AllowedMonthlyRemaining -----

    [Fact]
    public void AllowedMonthlyRemaining_equals_grand_total_when_days_positive()
    {
        var i = Budget(grandTotal: 500m, daysToNextPayday: 10m);
        // 500/10 = 50 per day × 10 days = 500
        Calc.AllowedMonthlyRemaining(i).Should().Be(500m);
    }

    [Fact]
    public void AllowedMonthlyRemaining_is_zero_when_no_days()
        => Calc.AllowedMonthlyRemaining(Budget(grandTotal: 500m, daysToNextPayday: 0m)).Should().Be(0m);

    // ----- AllowedWeeklyRemaining -----

    [Fact]
    public void AllowedWeeklyRemaining_full_week()
    {
        // 700 across 7 days = 100/day = 700/week
        var i = Budget(grandTotal: 700m, daysToNextPayday: 7m);
        Calc.AllowedWeeklyRemaining(i).Should().Be(700m);
    }

    [Fact]
    public void AllowedWeeklyRemaining_half_week()
    {
        // 350 across 3.5 days = 100/day = 700/week
        var i = Budget(grandTotal: 350m, daysToNextPayday: 3.5m);
        Calc.AllowedWeeklyRemaining(i).Should().Be(700m);
    }

    [Fact]
    public void AllowedWeeklyRemaining_zero_when_no_days()
        => Calc.AllowedWeeklyRemaining(Budget(grandTotal: 700m, daysToNextPayday: 0m)).Should().Be(0m);

    // ----- SpentPerDayToDate -----

    [Fact]
    public void SpentPerDayToDate_divides_spend_by_days_since()
    {
        var i = Budget(monthlySpendTotal: 50m, daysSincePayday: 5m);
        Calc.SpentPerDayToDate(i).Should().Be(10m);
    }

    [Fact]
    public void SpentPerDayToDate_is_zero_at_payday()
        => Calc.SpentPerDayToDate(Budget(monthlySpendTotal: 50m, daysSincePayday: 0m)).Should().Be(0m);

    [Fact]
    public void SpentPerDayToDate_clamps_negative_to_zero()
    {
        // Workbook edge: spend with positive sign (e.g. refund dominates) → clamp.
        var i = Budget(monthlySpendTotal: -20m, daysSincePayday: 5m);
        Calc.SpentPerDayToDate(i).Should().Be(0m);
    }

    // ----- EstimatedSpend -----

    [Fact]
    public void EstimatedSpend_projects_current_rate_to_payday()
    {
        var i = Budget(monthlySpendTotal: 100m, daysSincePayday: 5m, daysToNextPayday: 10m);
        // rate 20/day × 10 days remaining = 200
        Calc.EstimatedSpend(i).Should().Be(200m);
    }

    // ----- OverUnder -----

    [Fact]
    public void OverUnder_negative_when_on_track_to_underspend()
    {
        // allowed: 500/10 = 50/day; actual: 100/5 = 20/day; est 200; allowed 500; under 300 → -300
        var i = Budget(grandTotal: 500m, monthlySpendTotal: 100m, daysSincePayday: 5m, daysToNextPayday: 10m);
        Calc.OverUnder(i).Should().Be(-300m);
    }

    [Fact]
    public void OverUnder_zero_when_exactly_on_pace()
    {
        // spend so far 100 in 5 days → 20/day; remaining 10 days → est 200 left; grandTotal is 200 left
        var i = Budget(grandTotal: 200m, monthlySpendTotal: 100m, daysSincePayday: 5m, daysToNextPayday: 10m);
        Calc.OverUnder(i).Should().Be(0m);
    }

    [Fact]
    public void OverUnder_positive_when_on_track_to_overshoot()
    {
        // allowed: 200/10 = 20/day; actual: 150/5 = 30/day; est 300; allowed 200; over 100.
        var i = Budget(grandTotal: 200m, monthlySpendTotal: 150m, daysSincePayday: 5m, daysToNextPayday: 10m);
        Calc.OverUnder(i).Should().Be(100m);
    }

    // ----- EstimatedSpendWithBuffer / EstimatedLeftAfterBuffer -----

    [Fact]
    public void EstimatedSpendWithBuffer_combines_burn_rate_and_buffer()
    {
        // spent so far 100 / 5 days = 20/day burn; 10 days remain * £10 buffer = £100.
        // Formula: (SpentPerDayToDate * DaysSincePayday) + (BufferPerDay * DaysToNextPayday)
        // = (20 * 5) + (10 * 10) = 100 + 100 = 200.
        var i = Budget(
            grandTotal: 500m, monthlySpendTotal: 100m,
            daysSincePayday: 5m, daysToNextPayday: 10m, bufferPerDay: 10m);
        Calc.EstimatedSpendWithBuffer(i).Should().Be(200m);
    }

    [Fact]
    public void EstimatedLeftAfterBuffer_subtracts_from_grand_total()
    {
        // GrandTotal 500; EstimatedSpendWithBuffer 200 (from above); left 300.
        var i = Budget(
            grandTotal: 500m, monthlySpendTotal: 100m,
            daysSincePayday: 5m, daysToNextPayday: 10m, bufferPerDay: 10m);
        Calc.EstimatedLeftAfterBuffer(i).Should().Be(300m);
    }

    // ----- ExtraSpendToSave -----

    [Fact]
    public void ExtraSpendToSave_returns_headroom_when_allowance_above_buffer()
    {
        // 50/day allowance - 10 buffer = 40 headroom × 10 days = 400
        var i = Budget(grandTotal: 500m, daysToNextPayday: 10m, bufferPerDay: 10m);
        Calc.ExtraSpendToSave(i).Should().Be(400m);
    }

    [Fact]
    public void ExtraSpendToSave_zero_when_allowance_below_buffer()
    {
        // 5/day < 10 buffer → 0
        var i = Budget(grandTotal: 50m, daysToNextPayday: 10m, bufferPerDay: 10m);
        Calc.ExtraSpendToSave(i).Should().Be(0m);
    }

    [Fact]
    public void ExtraSpendToSave_zero_when_zero_days()
        => Calc.ExtraSpendToSave(Budget(grandTotal: 500m, daysToNextPayday: 0m, bufferPerDay: 10m)).Should().Be(0m);

    // ----- Weekly food sub-budget -----

    [Fact]
    public void WeeklyFoodSpendSoFar_flips_sign()
        => Calc.WeeklyFoodSpendSoFar(foodSpentSoFar: -80m, weeksSincePayday: 2m).Should().Be(40m);

    [Fact]
    public void WeeklyFoodSpendSoFar_zero_weeks_is_zero()
        => Calc.WeeklyFoodSpendSoFar(-80m, 0m).Should().Be(0m);

    [Fact]
    public void AllowedWeeklyFoodRemaining_flips_sign()
        => Calc.AllowedWeeklyFoodRemaining(foodBudgetRemaining: -180m, weeksUntilPayday: 3m).Should().Be(60m);

    [Fact]
    public void EstimatedRequiredFoodSpend_multiplies()
        => Calc.EstimatedRequiredFoodSpend(weeksBetweenPaydays: 4m, weeklyFoodBudget: 45m).Should().Be(180m);

    // ----- Debt utilisation -----

    [Fact]
    public void DebtUtilisation_averages_across_cards()
    {
        var debts = new[]
        {
            (BalanceAfter: -500m, Limit: 2000m),
            (BalanceAfter: -1000m, Limit: 3000m),
        };
        // outstanding = 1500; limits = 5000; 0.3 = 30%
        Calc.DebtUtilisation(debts).Should().Be(0.3m);
    }

    [Fact]
    public void DebtUtilisation_zero_when_no_debt()
        => Calc.DebtUtilisation([]).Should().Be(0m);

    [Fact]
    public void DebtUtilisation_zero_when_no_limits()
        => Calc.DebtUtilisation([(BalanceAfter: -500m, Limit: 0m)]).Should().Be(0m);

    // ----- Savings snowball -----

    [Fact]
    public void SavingsSnowballOneMonth_compounds()
    {
        // opening 1000, contribution 100, annual rate 0.06 → monthly 0.005
        // (1000 + 100) * 1.005 = 1105.5
        Calc.SavingsSnowballOneMonth(1000m, 100m, 0.06m).Should().Be(1105.50m);
    }

    [Fact]
    public void SavingsSnowballOneMonth_zero_rate_just_adds()
        => Calc.SavingsSnowballOneMonth(1000m, 100m, 0m).Should().Be(1100m);

    // ----- helper -----

    private static BudgetInputs Budget(
        decimal grandTotal = 0m,
        decimal monthlySpendTotal = 0m,
        decimal daysSincePayday = 0m,
        decimal daysToNextPayday = 0m,
        decimal bufferPerDay = 10m)
        => new(grandTotal, monthlySpendTotal, daysSincePayday, daysToNextPayday, bufferPerDay);
}
