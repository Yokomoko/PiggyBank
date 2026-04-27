using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Analytics;

/// <summary>
/// Side-by-side compare of two months — pick a Left and Right from the
/// month history and render headline metrics plus per-category spend with
/// a delta column. Read-only; nothing in the live database changes here.
/// </summary>
/// <remarks>
/// Loads both months in parallel using the same repositories and total-
/// computation as <see cref="PastMonthViewModel"/>, so figures match the
/// live dashboard exactly. Category rollup is the union of both months —
/// a category present only on one side appears with the other side at £0.
/// </remarks>
public sealed partial class CompareMonthsViewModel(
    IProfileSessionManager sessions,
    BudgetCalculator calc) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly BudgetCalculator _calc = calc;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public ObservableCollection<MonthOption> AvailableMonths { get; } = [];
    public ObservableCollection<CompareMetric> Metrics { get; } = [];
    public ObservableCollection<CompareCategory> Categories { get; } = [];

    [ObservableProperty] private MonthOption? _leftMonth;
    [ObservableProperty] private MonthOption? _rightMonth;
    [ObservableProperty] private bool _hasResults;

    /// <summary>Populates the dropdowns and pre-selects the two most recent
    /// months. Call once after the dialog opens; subsequent re-compares run
    /// through <see cref="CompareCommand"/>.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var monthsRepo = scope.GetRequiredService<IMonthRepository>();

        var months = (await monthsRepo.ListAsync(ct))
            .OrderByDescending(m => m.PeriodStart)
            .Select(m => new MonthOption(
                m.Id,
                $"{m.PeriodStart:MMM yyyy} ({m.PeriodStart:dd MMM} – {m.PeriodEnd:dd MMM})"))
            .ToList();

        AvailableMonths.Clear();
        foreach (var m in months) AvailableMonths.Add(m);

        // Preselect the two most recent so the dialog has data on first paint.
        // If only one month exists, leave Right null and the user picks once
        // a second month is added.
        LeftMonth = months.Count > 1 ? months[1] : months.FirstOrDefault();
        RightMonth = months.FirstOrDefault();

        if (LeftMonth is not null && RightMonth is not null)
            await CompareAsync(ct);
    }

    /// <summary>Recomputes both sides of the comparison. Auto-fired when
    /// the user changes either dropdown.</summary>
    [RelayCommand]
    public async Task CompareAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || LeftMonth is null || RightMonth is null) return;

        var left = await LoadMonthAsync(LeftMonth.Id, ct);
        var right = await LoadMonthAsync(RightMonth.Id, ct);
        if (left is null || right is null) return;

        Metrics.Clear();
        Metrics.Add(Metric("Salary",            left.Salary,            right.Salary));
        Metrics.Add(Metric("Outgoings",         left.Outgoings,         right.Outgoings));
        Metrics.Add(Metric("Ledger spend",      left.LedgerSpend,       right.LedgerSpend));
        Metrics.Add(Metric("Carried over",      left.CarriedOver,       right.CarriedOver));
        Metrics.Add(Metric("Running balance",   left.GrandTotal,        right.GrandTotal));
        Metrics.Add(Metric("Projected savings", left.ProjectedSavings,  right.ProjectedSavings));

        // Union categories so a row that only exists on one side still
        // shows. Sort by absolute delta descending — biggest movers first
        // is what the user is looking for here.
        var leftMap = left.CategorySpend;
        var rightMap = right.CategorySpend;
        var allNames = new HashSet<string>(leftMap.Keys);
        allNames.UnionWith(rightMap.Keys);

        Categories.Clear();
        foreach (var name in allNames
            .Select(n => new
            {
                Name = n,
                L = leftMap.GetValueOrDefault(n),
                R = rightMap.GetValueOrDefault(n),
            })
            .OrderByDescending(x => Math.Abs(x.R - x.L)))
        {
            Categories.Add(new CompareCategory(name.Name,
                Money(name.L), Money(name.R), DeltaDisplay(name.R - name.L)));
        }

        HasResults = true;
    }

    partial void OnLeftMonthChanged(MonthOption? value) => _ = RecomputeIfReady();
    partial void OnRightMonthChanged(MonthOption? value) => _ = RecomputeIfReady();

    private Task RecomputeIfReady()
        => LeftMonth is not null && RightMonth is not null ? CompareAsync() : Task.CompletedTask;

    private async Task<MonthSnapshot?> LoadMonthAsync(Guid monthId, CancellationToken ct)
    {
        var scope = _sessions.Current!.Services;
        var monthsRepo = scope.GetRequiredService<IMonthRepository>();
        var outgoingsRepo = scope.GetRequiredService<IMonthlyOutgoingRepository>();
        var txRepo = scope.GetRequiredService<ITransactionRepository>();

        var month = await monthsRepo.FindAsync(monthId, ct);
        if (month is null) return null;

        var outgoings = await outgoingsRepo.ListForMonthAsync(monthId, ct);
        var txs = await txRepo.ListForMonthAsync(monthId, ct);
        var rollups = await txRepo.SumByCategoryForMonthAsync(monthId, ct);

        var salary = month.MonthlySalary ?? 0m;
        var outgoingsTotal = outgoings.Sum(o => o.Amount);
        var ledger = txs.Sum(t => t.Amount);
        var spendMagnitude = -ledger;
        var grand = _calc.GrandTotal(salary + outgoingsTotal, spendMagnitude, month.CarriedOverBalance);

        var daysToNext = Math.Max(0m,
            (decimal)(month.NextPayday.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber));
        var daysSince = Math.Max(0m,
            (decimal)(DateOnly.FromDateTime(DateTime.Today).DayNumber - month.LastPayday.DayNumber));
        var overUnder = _calc.OverUnder(new BudgetInputs(grand, spendMagnitude, daysSince, daysToNext, 10m));

        return new MonthSnapshot(
            Salary: salary,
            Outgoings: outgoingsTotal,
            LedgerSpend: ledger,
            CarriedOver: month.CarriedOverBalance,
            GrandTotal: grand,
            // Sign convention: positive = saving, negative = shortfall.
            // Mirrors the headline pill on PastMonthViewModel.
            ProjectedSavings: -overUnder,
            CategorySpend: rollups.ToDictionary(r => r.CategoryName ?? "Uncategorised", r => r.Total));
    }

    private static CompareMetric Metric(string label, decimal left, decimal right)
        => new(label, Money(left), Money(right), DeltaDisplay(right - left));

    private static string Money(decimal v) =>
        v.ToString("£#,##0.00;-£#,##0.00;-", EnGb);

    /// <summary>Formats the delta with explicit + or - so the eye picks up
    /// movement direction without parsing the value. Zero shows as a dash.</summary>
    private static string DeltaDisplay(decimal delta)
    {
        if (delta == 0m) return "—";
        var sign = delta > 0 ? "+" : "";
        return $"{sign}{delta.ToString("£#,##0.00;-£#,##0.00", EnGb)}";
    }

    private sealed record MonthSnapshot(
        decimal Salary,
        decimal Outgoings,
        decimal LedgerSpend,
        decimal CarriedOver,
        decimal GrandTotal,
        decimal ProjectedSavings,
        IReadOnlyDictionary<string, decimal> CategorySpend);
}

public sealed record MonthOption(Guid Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record CompareMetric(string Label, string Left, string Right, string Delta);
public sealed record CompareCategory(string Name, string Left, string Right, string Delta);
