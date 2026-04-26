using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace PiggyBank.Data.Repositories;

public sealed class AnalyticsRepository(AppDbContext db) : IAnalyticsRepository
{
    public async Task<IReadOnlyList<MonthlySpendPoint>> GetMonthlySpendAsync(
        int monthCount = 12, CancellationToken ct = default)
    {
        // Group transactions by their owning Month, sum the spend (negative
        // amounts → flipped to positive magnitude), ordered newest first,
        // then take monthCount.
        var grouped = await db.Months
            .OrderByDescending(m => m.PeriodStart)
            .Take(monthCount)
            .Select(m => new
            {
                m.PeriodStart,
                Spend = db.Transactions
                    .Where(t => t.MonthId == m.Id && t.Amount < 0m)
                    .Sum(t => (decimal?)t.Amount) ?? 0m
            })
            .ToListAsync(ct);

        // Order oldest → newest for chart display.
        return grouped
            .OrderBy(g => g.PeriodStart)
            .Select(g => new MonthlySpendPoint(
                g.PeriodStart,
                g.PeriodStart.ToString("MMM yy", CultureInfo.GetCultureInfo("en-GB")),
                -g.Spend))
            .ToList();
    }

    public async Task<IReadOnlyList<CategorySpendPoint>> GetSpendByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= to
                        && t.Amount < 0m)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        var names = await db.Categories
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows
            .Select(r => new CategorySpendPoint(
                r.CategoryId is null
                    ? "Uncategorised"
                    : names.GetValueOrDefault(r.CategoryId.Value, "Unknown"),
                -r.Total))
            .OrderByDescending(r => r.Total)
            .ToList();
    }
}
