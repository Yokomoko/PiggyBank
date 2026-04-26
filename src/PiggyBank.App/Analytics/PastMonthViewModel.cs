using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Analytics;

/// <summary>
/// Read-only view of a previous (or current) month — opened from the
/// Analytics Months list. Loads the month's outgoings, ledger and totals
/// without any editing affordances. Reuses the same calc engine as the
/// live Current Month view so the figures match.
/// </summary>
public sealed partial class PastMonthViewModel(
    IProfileSessionManager sessions,
    BudgetCalculator calc) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly BudgetCalculator _calc = calc;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Set by the opening view before <see cref="LoadCommand"/> fires.</summary>
    public Guid MonthId { get; set; }

    public ObservableCollection<PastOutgoingRow> Outgoings { get; } = [];
    public ObservableCollection<PastTransactionRow> Transactions { get; } = [];
    public ObservableCollection<PastCategoryRollup> CategoryRollups { get; } = [];

    [ObservableProperty] private string _periodLabel = "";
    [ObservableProperty] private string _statusLabel = "";
    [ObservableProperty] private decimal _monthlySalary;
    [ObservableProperty] private decimal _carriedOverBalance;
    [ObservableProperty] private decimal _totalOutgoings;
    [ObservableProperty] private decimal _monthlySpendTotal;
    [ObservableProperty] private decimal _grandTotal;
    [ObservableProperty] private decimal _projectedSavings;
    [ObservableProperty] private string _savingsHeadline = "";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var monthsRepo = scope.GetRequiredService<IMonthRepository>();
        var outgoingsRepo = scope.GetRequiredService<IMonthlyOutgoingRepository>();
        var txRepo = scope.GetRequiredService<ITransactionRepository>();
        var categories = scope.GetRequiredService<ICategoryRepository>();

        var month = await monthsRepo.FindAsync(MonthId, ct);
        if (month is null)
        {
            PeriodLabel = "Month not found.";
            return;
        }

        PeriodLabel = $"{month.PeriodStart:dd MMM yyyy} – {month.PeriodEnd:dd MMM yyyy}";
        StatusLabel = month.IsClosed ? "Closed" : "Open";
        MonthlySalary = month.MonthlySalary ?? 0m;
        CarriedOverBalance = month.CarriedOverBalance;

        Outgoings.Clear();
        var outgoings = await outgoingsRepo.ListForMonthAsync(month.Id, ct);
        foreach (var o in outgoings)
            Outgoings.Add(new PastOutgoingRow(o.Name, o.Amount, o.IsAllocation));

        var allCats = await categories.ListAsync(ct: ct);
        var catLookup = allCats.ToDictionary(c => c.Id, c => c.Name);

        Transactions.Clear();
        var txs = await txRepo.ListForMonthAsync(month.Id, ct);
        foreach (var t in txs.OrderByDescending(t => t.Date))
        {
            var catName = t.CategoryId is Guid cid && catLookup.TryGetValue(cid, out var n) ? n : "";
            Transactions.Add(new PastTransactionRow(t.Date, t.Payee, t.Amount, catName));
        }

        CategoryRollups.Clear();
        var rollups = await txRepo.SumByCategoryForMonthAsync(month.Id, ct);
        foreach (var r in rollups.OrderBy(r => r.Total))  // most-negative (biggest spend) first
            CategoryRollups.Add(new PastCategoryRollup(r.CategoryName ?? "Uncategorised", r.Total));

        // Totals — same maths as Current Month, sign-corrected like the
        // recent fix to BudgetCalculator's API.
        TotalOutgoings = outgoings.Sum(o => o.Amount);
        var total = MonthlySalary + TotalOutgoings;
        MonthlySpendTotal = txs.Sum(t => t.Amount);
        var spendMagnitude = -MonthlySpendTotal;
        GrandTotal = _calc.GrandTotal(total, spendMagnitude, CarriedOverBalance);

        var daysToNext = Math.Max(0m, (decimal)(month.NextPayday.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber));
        var daysSince = Math.Max(0m, (decimal)(DateOnly.FromDateTime(DateTime.Today).DayNumber - month.LastPayday.DayNumber));
        var inputs = new BudgetInputs(GrandTotal, spendMagnitude, daysSince, daysToNext, 10m);
        var overUnder = _calc.OverUnder(inputs);
        ProjectedSavings = Math.Abs(overUnder);
        SavingsHeadline = overUnder switch
        {
            < 0m => "PROJECTED TO SAVE",
            > 0m => "PROJECTED SHORTFALL",
            _    => "TRACKING TO BUDGET",
        };
    }
}

public sealed record PastOutgoingRow(string Name, decimal Amount, bool IsAllocation)
{
    public string AmountDisplay => Amount.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
}

public sealed record PastTransactionRow(DateOnly Date, string Payee, decimal Amount, string Category)
{
    public string AmountDisplay => Amount.ToString("£#,##0.00;-£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));
}

public sealed record PastCategoryRollup(string Name, decimal Total)
{
    public string TotalDisplay => Total.ToString("£#,##0.00;-£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));
}
