namespace PiggyBank.Data.Repositories;

public interface IAnalyticsRepository
{
    /// <summary>Net spend (sum of negative <see cref="Transaction.Amount"/>
    /// values, returned as a positive magnitude) for each of the last
    /// <paramref name="monthCount"/> months with any transactions.</summary>
    Task<IReadOnlyList<MonthlySpendPoint>> GetMonthlySpendAsync(
        int monthCount = 12, CancellationToken ct = default);

    /// <summary>Spend by category for every transaction in the given period.
    /// Income transactions (positive amounts) are excluded — this view is
    /// about outflows.</summary>
    Task<IReadOnlyList<CategorySpendPoint>> GetSpendByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record MonthlySpendPoint(DateOnly PeriodStart, string Label, decimal Total);
public sealed record CategorySpendPoint(string CategoryName, decimal Total);
