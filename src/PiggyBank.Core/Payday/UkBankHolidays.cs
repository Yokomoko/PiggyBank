namespace PiggyBank.Core.Payday;

/// <summary>
/// Hardcoded England &amp; Wales bank-holiday dates. Covers a comfortable
/// rolling window; refresh annually from
/// <c>https://www.gov.uk/bank-holidays.json</c> (England &amp; Wales division).
/// Kept in-code rather than a network call because payday math must be
/// deterministic and offline.
/// </summary>
public static class UkBankHolidays
{
    private static readonly HashSet<DateOnly> _dates =
    [
        // 2024
        new(2024, 1, 1),   // New Year's Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 4, 1),   // Easter Monday
        new(2024, 5, 6),   // Early May bank holiday
        new(2024, 5, 27),  // Spring bank holiday
        new(2024, 8, 26),  // Summer bank holiday
        new(2024, 12, 25), // Christmas Day
        new(2024, 12, 26), // Boxing Day

        // 2025
        new(2025, 1, 1),
        new(2025, 4, 18),  // Good Friday
        new(2025, 4, 21),  // Easter Monday
        new(2025, 5, 5),
        new(2025, 5, 26),
        new(2025, 8, 25),
        new(2025, 12, 25),
        new(2025, 12, 26),

        // 2026
        new(2026, 1, 1),
        new(2026, 4, 3),   // Good Friday
        new(2026, 4, 6),   // Easter Monday
        new(2026, 5, 4),
        new(2026, 5, 25),
        new(2026, 8, 31),
        new(2026, 12, 25),
        new(2026, 12, 28), // Boxing Day substitute (26th = Saturday)

        // 2027
        new(2027, 1, 1),
        new(2027, 3, 26),  // Good Friday
        new(2027, 3, 29),  // Easter Monday
        new(2027, 5, 3),
        new(2027, 5, 31),
        new(2027, 8, 30),
        new(2027, 12, 27), // Christmas substitute (25th = Saturday)
        new(2027, 12, 28), // Boxing Day substitute (26th = Sunday)

        // 2028
        new(2028, 1, 3),   // New Year substitute
        new(2028, 4, 14),
        new(2028, 4, 17),
        new(2028, 5, 1),
        new(2028, 5, 29),
        new(2028, 8, 28),
        new(2028, 12, 25),
        new(2028, 12, 26),

        // 2029
        new(2029, 1, 1),
        new(2029, 3, 30),
        new(2029, 4, 2),
        new(2029, 5, 7),
        new(2029, 5, 28),
        new(2029, 8, 27),
        new(2029, 12, 25),
        new(2029, 12, 26),

        // 2030
        new(2030, 1, 1),
        new(2030, 4, 19),
        new(2030, 4, 22),
        new(2030, 5, 6),
        new(2030, 5, 27),
        new(2030, 8, 26),
        new(2030, 12, 25),
        new(2030, 12, 26),
    ];

    public static bool IsBankHoliday(DateOnly date) => _dates.Contains(date);

    /// <summary>Max year represented. Callers should warn if queries sail past this.</summary>
    public static int LatestYearCovered => 2030;
}
