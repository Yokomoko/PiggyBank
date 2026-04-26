namespace PiggyBank.Core.Budgeting;

/// <summary>
/// Pure functions that port the Money Manager spreadsheet's allowance
/// engine (see <c>PiggyBank-WPF-Spec.md §2.3</c>). Every method
/// corresponds to a named cell in the workbook; cell references are
/// in the XML-doc comments so test failures are traceable to source.
///
/// All arithmetic uses <see cref="decimal"/> (stricter precision than
/// Excel's <see cref="double"/>). Results are NOT rounded here — the
/// view layer formats to 2 d.p. Tests assert to within £0.01.
/// </summary>
public sealed class BudgetCalculator(TimeProvider clock)
{
    // Retained for future use — e.g. payday-derived calculations that
    // need "today" when the caller doesn't supply it.
    private readonly TimeProvider _clock = clock;

    /// <summary>B21: Disposable budget for the pay period. Equals Total net of bills (B18),
    /// minus spend-so-far (B19), plus any balance rolled over from prior month (B20).</summary>
    public decimal GrandTotal(decimal total, decimal monthlySpendTotal, decimal carriedOver)
        => total - monthlySpendTotal + carriedOver;

    /// <summary>B30: How much can the user spend today without dipping into next month?
    /// Returns zero if the pay period has ended (no days remaining).</summary>
    public decimal AllowedSpendPerDay(BudgetInputs i)
        => i.DaysToNextPayday <= 0m ? 0m : i.GrandTotal / i.DaysToNextPayday;

    /// <summary>B31: Same but using whole days (round up fractional day),
    /// so "0.3 days left" still yields a sane number (divides by 1, not 0.3).</summary>
    public decimal AllowedSpendWholeDays(BudgetInputs i)
    {
        var wholeDays = Math.Ceiling(i.DaysToNextPayday);
        return wholeDays <= 0m ? 0m : i.GrandTotal / wholeDays;
    }

    /// <summary>B34: Allowed spend across the remaining days. Equivalent to
    /// GrandTotal when DaysToNextPayday &gt; 0; zero otherwise.</summary>
    public decimal AllowedMonthlyRemaining(BudgetInputs i)
        => AllowedSpendPerDay(i) * i.DaysToNextPayday;

    /// <summary>B32: Weekly allowance during the remaining period.
    /// Formula: AllowedMonthlyRemaining ÷ (daysRemaining ÷ 7).</summary>
    public decimal AllowedWeeklyRemaining(BudgetInputs i)
    {
        if (i.DaysToNextPayday <= 0m) return 0m;
        var weeksRemaining = i.DaysToNextPayday / 7m;
        return weeksRemaining == 0m ? 0m : AllowedMonthlyRemaining(i) / weeksRemaining;
    }

    /// <summary>B33: Actual burn rate so far this period. Clamped to zero if
    /// the sign flips (which happens when spend exceeds budget + carry-over).</summary>
    public decimal SpentPerDayToDate(BudgetInputs i)
    {
        if (i.DaysSincePayday <= 0m) return 0m;
        var rate = i.MonthlySpendTotal / i.DaysSincePayday;
        return rate < 0m ? 0m : rate;
    }

    /// <summary>B35: If the user continues at today's rate, what will they have spent
    /// by payday? <c>SpentPerDayToDate × DaysToNextPayday</c>.</summary>
    public decimal EstimatedSpend(BudgetInputs i)
        => SpentPerDayToDate(i) * i.DaysToNextPayday;

    /// <summary>B36: Positive = on track to overshoot by this amount;
    /// negative = on track to have this much left.</summary>
    public decimal OverUnder(BudgetInputs i)
        => EstimatedSpend(i) - AllowedMonthlyRemaining(i);

    /// <summary>B38: If the user's daily allowance is above the buffer threshold,
    /// the "extra" portion is labelled as savable without impacting the month.
    /// Zero when allowance ≤ buffer.</summary>
    public decimal ExtraSpendToSave(BudgetInputs i)
    {
        var headroom = AllowedSpendPerDay(i) - i.BufferPerDay;
        return headroom > 0m ? headroom * i.DaysToNextPayday : 0m;
    }

    /// <summary>B39: Estimated spend if the user keeps today's rate AND
    /// uses the full buffer every remaining day.</summary>
    public decimal EstimatedSpendWithBuffer(BudgetInputs i)
        => (SpentPerDayToDate(i) * i.DaysSincePayday)
           + (i.BufferPerDay * i.DaysToNextPayday);

    /// <summary>B40: What will be left in the pot after assumed buffer spend?
    /// GrandTotal − EstimatedSpendWithBuffer.</summary>
    public decimal EstimatedLeftAfterBuffer(BudgetInputs i)
        => i.GrandTotal - EstimatedSpendWithBuffer(i);

    /// <summary>B44: Weekly food spend so far. <c>FoodSpendSoFar ÷ WeeksSincePayday</c>
    /// (sign-inverted in the workbook because food spend is stored negative).</summary>
    public decimal WeeklyFoodSpendSoFar(decimal foodSpentSoFar, decimal weeksSincePayday)
    {
        if (weeksSincePayday <= 0m) return 0m;
        // Workbook convention: foodSpentSoFar is stored as a negative decimal.
        // Flip sign so the output reads as a positive "weekly burn".
        return -(foodSpentSoFar / weeksSincePayday);
    }

    /// <summary>B45: Remaining weekly food budget given a fixed food allocation and
    /// the remaining weeks. <c>FoodBudgetRemaining ÷ WeeksUntilPayday</c>.</summary>
    public decimal AllowedWeeklyFoodRemaining(decimal foodBudgetRemaining, decimal weeksUntilPayday)
    {
        if (weeksUntilPayday <= 0m) return 0m;
        return -(foodBudgetRemaining / weeksUntilPayday);
    }

    /// <summary>B47: Simple target = weeksBetweenPaydays × ProfileSettings.DailyFoodBudget × 7/5 heuristic.
    /// Workbook hard-codes £45/week; we parameterise it.</summary>
    public decimal EstimatedRequiredFoodSpend(decimal weeksBetweenPaydays, decimal weeklyFoodBudget)
        => weeksBetweenPaydays * weeklyFoodBudget;

    /// <summary>Debt block N17: Combined utilisation across credit cards.
    /// <c>SUM(abs(AfterBalances)) ÷ SUM(Limits)</c>. Returns zero when no limit.</summary>
    public decimal DebtUtilisation(IEnumerable<(decimal BalanceAfter, decimal Limit)> debts)
    {
        decimal outstanding = 0m, limits = 0m;
        foreach (var (balanceAfter, limit) in debts)
        {
            outstanding += Math.Abs(balanceAfter);
            limits += limit;
        }
        return limits == 0m ? 0m : outstanding / limits;
    }

    /// <summary>Savings sheet F: <c>(OpeningValue + MonthlyContribution) × (1 + MonthlyRate)</c>.
    /// Used by <c>v_SavingsRunning</c>'s C# materialisation.</summary>
    public decimal SavingsSnowballOneMonth(decimal openingValue, decimal monthlyContribution, decimal annualRate)
    {
        var monthlyRate = annualRate / 12m;
        return (openingValue + monthlyContribution) * (1m + monthlyRate);
    }
}

/// <summary>
/// Scalar inputs to the allowance engine. All derived from the workbook's
/// B-column cells; consumers populate from <c>Month</c> + <c>Transaction</c>
/// + <c>ProfileSettings</c>.
/// </summary>
public sealed record BudgetInputs(
    decimal GrandTotal,
    decimal MonthlySpendTotal,
    decimal DaysSincePayday,
    decimal DaysToNextPayday,
    decimal BufferPerDay);
