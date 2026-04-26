using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Budgeting;

/// <summary>
/// Pure functions over side-income entries + allocations. No IO, no state —
/// the VM calls these to render per-entry / per-calendar-month summaries.
/// </summary>
public static class SideIncomeMath
{
    /// <summary>Suggested total from duration × rate. Returns null if either
    /// input is missing so callers can fall back to a user-entered total.</summary>
    public static decimal? SuggestedTotal(decimal? durationHours, decimal? hourlyRate)
        => durationHours is decimal h && hourlyRate is decimal r
            ? Math.Round(h * r, 2, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>Remaining = Total − Σ allocations for this entry. Clamped
    /// to zero so a data-integrity bug (over-allocation) doesn't render a
    /// negative "remaining" that confuses the user.</summary>
    public static decimal RemainingFor(
        SideIncomeEntry entry,
        IEnumerable<SideIncomeAllocation> allocations)
    {
        var allocated = allocations
            .Where(a => a.SideIncomeEntryId == entry.Id)
            .Sum(a => a.Amount);
        return Math.Max(0m, entry.Total - allocated);
    }

    /// <summary>Groups entries + their allocations by the calendar month of
    /// PaidOn. Ordered newest-first. Each group exposes total earned,
    /// allocated, and remaining.</summary>
    public static IReadOnlyList<SideIncomeCalendarMonth> GroupByCalendarMonth(
        IEnumerable<SideIncomeEntry> entries,
        IEnumerable<SideIncomeAllocation> allocations)
    {
        var allocsByEntry = allocations
            .GroupBy(a => a.SideIncomeEntryId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SideIncomeAllocation>)g.ToList());

        return entries
            .GroupBy(e => new DateOnly(e.PaidOn.Year, e.PaidOn.Month, 1))
            .Select(g =>
            {
                var earned = g.Sum(e => e.Total);
                var allocated = g.Sum(e => allocsByEntry.TryGetValue(e.Id, out var a)
                    ? a.Sum(x => x.Amount)
                    : 0m);
                return new SideIncomeCalendarMonth(
                    g.Key,
                    earned,
                    allocated,
                    Math.Max(0m, earned - allocated),
                    g.OrderByDescending(e => e.PaidOn).ThenByDescending(e => e.CreatedAtUtc).ToList());
            })
            .OrderByDescending(m => m.PeriodStart)
            .ToList();
    }
}

/// <summary>Per-calendar-month aggregate of side-income entries.</summary>
public sealed record SideIncomeCalendarMonth(
    DateOnly PeriodStart,
    decimal TotalEarned,
    decimal TotalAllocated,
    decimal Remaining,
    IReadOnlyList<SideIncomeEntry> Entries);
