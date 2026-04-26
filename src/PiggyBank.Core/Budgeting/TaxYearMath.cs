using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Budgeting;

/// <summary>
/// UK tax-year boundary handling and ballpark side-income set-aside
/// estimates. Tax year runs 6 April to 5 April. Rates are rule-of-thumb
/// (income tax + Class 4 NI combined) — not a substitute for filing.
/// </summary>
public static class TaxYearMath
{
    /// <summary>Returns the START year of the tax year that contains the
    /// given date. e.g. 5 Apr 2027 → 2026, 6 Apr 2027 → 2027.</summary>
    public static int TaxYearStartYearOf(DateOnly date)
    {
        var apr6ThisYear = new DateOnly(date.Year, 4, 6);
        return date >= apr6ThisYear ? date.Year : date.Year - 1;
    }

    /// <summary>Formats the tax year label as "YYYY/YY" (e.g. 2026 → "2026/27").</summary>
    public static string Label(int startYear)
        => $"{startYear}/{(startYear + 1) % 100:00}";

    /// <summary>Bundles all entries by tax year, then estimates set-aside
    /// per group using the configured band. Newest year first.</summary>
    public static IReadOnlyList<TaxYearSummary> SummariseByTaxYear(
        IEnumerable<SideIncomeEntry> entries,
        TaxBand band,
        decimal? customRate)
    {
        return entries
            .GroupBy(e => TaxYearStartYearOf(e.PaidOn))
            .OrderByDescending(g => g.Key)
            .Select(g => Estimate(g.Key, g.Sum(e => e.Total), band, customRate))
            .ToList();
    }

    /// <summary>Compute the set-aside estimate for one tax year's total earnings.</summary>
    public static TaxYearSummary Estimate(
        int taxYearStart,
        decimal totalEarned,
        TaxBand band,
        decimal? customRate)
    {
        // (effective rate, taxable base, caption)
        (decimal rate, decimal taxable, string note) = band switch
        {
            TaxBand.TradingAllowance when totalEarned <= 1000m =>
                (0m, 0m, "Within £1,000 trading allowance — no tax owed."),
            TaxBand.TradingAllowance =>
                (0.26m, totalEarned - 1000m,
                    "Crossed £1,000 trading allowance — basic rate on the excess."),
            TaxBand.Basic =>
                (0.26m, totalEarned, "20% income tax + 6% Class 4 NI."),
            TaxBand.Higher =>
                (0.42m, totalEarned, "40% + 2% NI."),
            TaxBand.Additional =>
                (0.47m, totalEarned, "45% + 2% NI."),
            TaxBand.Custom =>
                (customRate ?? 0m, totalEarned,
                    $"Custom rate {((customRate ?? 0m) * 100m):0.#}%."),
            _ => (0m, 0m, ""),
        };

        var setAside = Math.Round(taxable * rate, 2, MidpointRounding.AwayFromZero);
        return new TaxYearSummary(taxYearStart, Label(taxYearStart),
            totalEarned, setAside, rate, note);
    }
}

public sealed record TaxYearSummary(
    int StartYear,
    string Label,
    decimal TotalEarned,
    decimal SetAside,
    decimal EffectiveRate,
    string Note);
