namespace PiggyBank.Core.Payday;

/// <summary>
/// Resolves a profile's "primary payday" for a given calendar month,
/// applying the standard UK rule: if the nominal day falls on a weekend
/// or England &amp; Wales bank holiday, shift BACK to the previous working
/// day (Monday through Friday, non-holiday). Examples:
/// <list type="bullet">
///   <item>25th = Saturday → pay lands on Friday 24th.</item>
///   <item>24th = Sunday → pay lands on Friday 22nd.</item>
///   <item>24th = Wednesday = bank holiday → pay lands on Tuesday 23rd.</item>
/// </list>
/// </summary>
public static class PaydayCalculator
{
    /// <summary>
    /// Computes the actual payday for the month containing <paramref name="anchor"/>.
    /// If <paramref name="dayOfMonth"/> is larger than the number of days in
    /// the month (e.g. 31 in February), clamps to the last day of the month
    /// before applying adjustments.
    /// </summary>
    public static DateOnly ResolveForMonth(
        int year, int month, int dayOfMonth, bool adjustForWeekendsAndBankHolidays)
    {
        if (dayOfMonth < 1 || dayOfMonth > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Must be 1–31.");

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var clampedDay = Math.Min(dayOfMonth, daysInMonth);
        var nominal = new DateOnly(year, month, clampedDay);

        if (!adjustForWeekendsAndBankHolidays)
            return nominal;

        return ShiftToPreviousWorkingDay(nominal);
    }

    /// <summary>Same, but takes an explicit "anchor" date and uses its month.</summary>
    public static DateOnly ResolveForMonth(
        DateOnly anchor, int dayOfMonth, bool adjustForWeekendsAndBankHolidays)
        => ResolveForMonth(anchor.Year, anchor.Month, dayOfMonth, adjustForWeekendsAndBankHolidays);

    /// <summary>
    /// Back-solve for the pay window containing <paramref name="today"/>.
    /// Returns (lastPayday, nextPayday) where lastPayday ≤ today &lt; nextPayday.
    /// </summary>
    public static (DateOnly LastPayday, DateOnly NextPayday) ResolvePayWindow(
        DateOnly today, int dayOfMonth, bool adjustForWeekendsAndBankHolidays)
    {
        var thisMonthPayday = ResolveForMonth(today, dayOfMonth, adjustForWeekendsAndBankHolidays);

        if (today >= thisMonthPayday)
        {
            var nextAnchor = today.AddMonths(1);
            var next = ResolveForMonth(nextAnchor, dayOfMonth, adjustForWeekendsAndBankHolidays);
            return (thisMonthPayday, next);
        }

        var priorAnchor = today.AddMonths(-1);
        var prior = ResolveForMonth(priorAnchor, dayOfMonth, adjustForWeekendsAndBankHolidays);
        return (prior, thisMonthPayday);
    }

    private static DateOnly ShiftToPreviousWorkingDay(DateOnly date)
    {
        var cursor = date;
        while (IsWeekend(cursor) || UkBankHolidays.IsBankHoliday(cursor))
            cursor = cursor.AddDays(-1);
        return cursor;
    }

    private static bool IsWeekend(DateOnly d) =>
        d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
