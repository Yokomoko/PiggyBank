using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Analytics.Reports;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Profiles;
using PiggyBank.Data.Repositories;
using QuestPDF.Fluent;

namespace PiggyBank.App.Analytics;

public sealed partial class AnalyticsViewModel(
    IProfileSessionManager sessions,
    BudgetCalculator calc) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly BudgetCalculator _calc = calc;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private int _rangeMonths = 12;
    [ObservableProperty] private string _rangeLabel = "Last 12 months";
    [ObservableProperty] private MonthSummaryRow? _selectedMonth;

    /// <summary>Raised when the user clicks "View" on a Months row — view
    /// hosts the modal so the VM stays testable.</summary>
    public event EventHandler<MonthSummaryRow>? ViewMonthRequested;

    /// <summary>Raised when the user clicks "Compare months" — view hosts
    /// the modal. No payload: the dialog populates its own pickers.</summary>
    public event EventHandler? CompareMonthsRequested;

    [RelayCommand]
    public void ViewMonth(MonthSummaryRow? row)
    {
        if (row is null) return;
        ViewMonthRequested?.Invoke(this, row);
    }

    [RelayCommand]
    public void CompareMonths() => CompareMonthsRequested?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<ISeries> MonthlySpendSeries { get; } = [];
    public ObservableCollection<Axis> MonthlySpendXAxis { get; } = [];
    public ObservableCollection<Axis> MonthlySpendYAxis { get; } = [];

    public ObservableCollection<ISeries> CategorySpendSeries { get; } = [];
    public ObservableCollection<Axis> CategorySpendXAxis { get; } = [];
    public ObservableCollection<Axis> CategorySpendYAxis { get; } = [];

    public ObservableCollection<MonthSummaryRow> Months { get; } = [];

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            var scope = _sessions.Current.Services;
            var analytics = scope.GetRequiredService<IAnalyticsRepository>();
            var monthsRepo = scope.GetRequiredService<IMonthRepository>();

            var monthly = await analytics.GetMonthlySpendAsync(RangeMonths, ct);
            BuildMonthlySpendChart(monthly);

            var now = DateTime.Today;
            var to = DateOnly.FromDateTime(now);
            var from = DateOnly.FromDateTime(now.AddMonths(-RangeMonths));
            var categories = await analytics.GetSpendByCategoryAsync(from, to, ct);
            BuildCategoryChart(categories);

            // Month history list — ListAsync returns everything; cap to the
            // active range so the Months card stays scoped to what charts show.
            var monthEntities = (await monthsRepo.ListAsync(ct))
                .OrderByDescending(m => m.PeriodStart)
                .Take(RangeMonths)
                .ToList();
            var spendLookup = monthly.ToDictionary(m => m.PeriodStart, m => m.Total);
            Months.Clear();
            foreach (var m in monthEntities.OrderByDescending(x => x.PeriodStart))
            {
                spendLookup.TryGetValue(m.PeriodStart, out var net);
                Months.Add(new MonthSummaryRow(
                    m.Id,
                    $"{m.PeriodStart:dd MMM yyyy} – {m.PeriodEnd:dd MMM yyyy}",
                    m.IsClosed ? "Closed" : "Open",
                    net));
            }

            HasData = monthly.Count > 0 || categories.Count > 0;
            RangeLabel = $"Last {RangeMonths} months of activity";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task SetRangeAsync(string months)
    {
        if (int.TryParse(months, out var m)) RangeMonths = m;
        await LoadAsync();
    }

    /// <summary>Writes a CSV for the given month — outgoings + ledger transactions —
    /// to the user's chosen path. No external dependencies; plain file I/O.</summary>
    [RelayCommand]
    public async Task ExportMonthCsvAsync(MonthSummaryRow? row)
    {
        if (row is null || _sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var outgoingsRepo = scope.GetRequiredService<IMonthlyOutgoingRepository>();
        var txRepo = scope.GetRequiredService<ITransactionRepository>();

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"PiggyBank-{row.PeriodLabel.Replace(' ', '-').Replace('–', '-')}.csv",
            Filter = "CSV (*.csv)|*.csv",
            Title = "Export month to CSV",
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        var gb = CultureInfo.GetCultureInfo("en-GB");

        sb.AppendLine("Section,Date,Name/Payee,Category,Amount,IsAllocation");

        var outgoings = await outgoingsRepo.ListForMonthAsync(row.MonthId);
        foreach (var o in outgoings)
        {
            sb.AppendLine(string.Join(',',
                "Outgoing",
                "",
                Escape(o.Name),
                "",
                o.Amount.ToString("F2", gb),
                o.IsAllocation,
                ""));
        }

        var txs = await txRepo.ListForMonthAsync(row.MonthId);
        foreach (var t in txs)
        {
            sb.AppendLine(string.Join(',',
                "Transaction",
                t.Date.ToString("yyyy-MM-dd"),
                Escape(t.Payee),
                "",
                t.Amount.ToString("F2", gb),
                ""));
        }

        await File.WriteAllTextAsync(dlg.FileName, sb.ToString());
    }

    /// <summary>Builds a QuestPDF report for the chosen month — same data
    /// fetch pattern as the CSV export, plus categories + computed totals
    /// using <see cref="BudgetCalculator.GrandTotal(decimal,decimal,decimal)"/>.
    /// Saved via the standard SaveFileDialog. Rendering lives in
    /// <see cref="MonthPdfReport"/> — the VM does no QuestPDF work itself.</summary>
    [RelayCommand]
    public async Task ExportMonthPdfAsync(MonthSummaryRow? row)
    {
        if (row is null || _sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var monthsRepo = scope.GetRequiredService<IMonthRepository>();
        var outgoingsRepo = scope.GetRequiredService<IMonthlyOutgoingRepository>();
        var txRepo = scope.GetRequiredService<ITransactionRepository>();
        var categoriesRepo = scope.GetRequiredService<ICategoryRepository>();

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"PiggyBank-{row.PeriodLabel.Replace(' ', '-').Replace('–', '-')}.pdf",
            Filter = "PDF (*.pdf)|*.pdf",
            Title = "Export month to PDF",
        };
        if (dlg.ShowDialog() != true) return;

        var month = await monthsRepo.FindAsync(row.MonthId);
        if (month is null) return;

        var outgoings = await outgoingsRepo.ListForMonthAsync(month.Id);
        var transactions = await txRepo.ListForMonthAsync(month.Id);
        var rollups = await txRepo.SumByCategoryForMonthAsync(month.Id);
        var allCats = await categoriesRepo.ListAsync();
        var catLookup = allCats.ToDictionary(c => c.Id, c => c.Name);

        // Allocation drawdown — mirror Recalculate() so the report's
        // outgoings table shows realistic Used/Remaining figures for any
        // allocation row, NOT just the static template amount.
        var allocationCategoryIds = outgoings
            .Where(o => o.IsAllocation && o.CategoryId.HasValue)
            .Select(o => o.CategoryId!.Value)
            .ToHashSet();
        bool IsDrawdown(Transaction t) =>
            t.CategoryId.HasValue && allocationCategoryIds.Contains(t.CategoryId.Value);

        var pdfOutgoings = outgoings.Select(o =>
        {
            decimal used = 0m, remaining = 0m;
            if (o.IsAllocation && o.CategoryId.HasValue)
            {
                used = Math.Abs(transactions
                    .Where(t => t.CategoryId == o.CategoryId)
                    .Sum(t => t.Amount));
                remaining = Math.Abs(o.Amount) - used;
            }
            return new MonthPdfOutgoing(o.Name, o.Amount, o.IsAllocation, used, remaining);
        }).ToList();

        var pdfTransactions = transactions
            .OrderByDescending(t => t.Date)
            .Select(t => new MonthPdfTransaction(
                t.Date,
                t.Payee,
                t.CategoryId is Guid cid && catLookup.TryGetValue(cid, out var n) ? n : null,
                t.Amount))
            .ToList();

        var pdfRollups = rollups
            .OrderBy(r => r.Total)  // most-negative spend first
            .Select(r => new MonthPdfCategoryRollup(r.CategoryName ?? "Uncategorised", r.Total))
            .ToList();

        // Totals — same maths used by CurrentMonthViewModel.Recalculate, so
        // the figures on the PDF match the live dashboard.
        var totalOutgoings = outgoings.Sum(o => o.Amount);
        // Apply the same allocation-overspend rule the live view uses: when
        // used > allocated, the effective amount is -used (raises the bill).
        var effectiveOutgoingsTotal = outgoings.Sum(o =>
        {
            if (!o.IsAllocation || !o.CategoryId.HasValue) return o.Amount;
            var used = Math.Abs(transactions
                .Where(t => t.CategoryId == o.CategoryId)
                .Sum(t => t.Amount));
            var allocated = Math.Abs(o.Amount);
            return used > allocated ? -used : o.Amount;
        });
        var monthlySalary = month.MonthlySalary ?? 0m;
        var nonDrawdownSpend = transactions.Where(t => !IsDrawdown(t)).Sum(t => t.Amount);
        var total = monthlySalary + effectiveOutgoingsTotal;
        var grandTotal = _calc.GrandTotal(total, -nonDrawdownSpend, month.CarriedOverBalance);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysToNext = Math.Max(0m, (decimal)(month.NextPayday.DayNumber - today.DayNumber));
        var daysSince = Math.Max(0m, (decimal)(today.DayNumber - month.LastPayday.DayNumber));
        var inputs = new BudgetInputs(grandTotal, -nonDrawdownSpend, daysSince, daysToNext, 10m);
        var overUnder = _calc.OverUnder(inputs);
        var projectedSavings = Math.Abs(overUnder);
        var isProjectedPositive = overUnder < 0m;

        var displayName = await GetActiveProfileDisplayNameAsync() ?? "PiggyBank";
        var data = new MonthPdfReportData(
            ProfileDisplayName: displayName,
            PeriodLabel: row.PeriodLabel,
            IsClosed: month.IsClosed,
            MonthlySalary: monthlySalary,
            CarriedOverBalance: month.CarriedOverBalance,
            TotalOutgoings: totalOutgoings,
            MonthlySpendTotal: nonDrawdownSpend,
            GrandTotal: grandTotal,
            ProjectedSavings: projectedSavings,
            IsProjectedPositive: isProjectedPositive,
            Outgoings: pdfOutgoings,
            Transactions: pdfTransactions,
            CategoryRollups: pdfRollups,
            GeneratedAt: DateTime.Now);

        // QuestPDF's GeneratePdf is synchronous and CPU-bound — push it off
        // the UI thread so the SaveFileDialog return doesn't freeze the shell.
        // Surface failures via a MessageBox; an unhandled task exception here
        // would otherwise tear down the app on the UI thread.
        var path = dlg.FileName;
        try
        {
            await Task.Run(() => new MonthPdfReport(data).GeneratePdf(path));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not generate the PDF.\n\n{ex.Message}",
                "Export PDF",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Looks up the currently-open profile's DisplayName via the
    /// admin scope — the only place cross-profile data is permitted.
    /// Used purely to label the report header; failure is non-fatal.</summary>
    private async Task<string?> GetActiveProfileDisplayNameAsync()
    {
        if (_sessions.Current is null) return null;
        try
        {
            using var admin = _sessions.OpenAdminScope();
            var svc = admin.Services.GetRequiredService<ProfileAdminService>();
            var profile = await svc.FindAsync(_sessions.Current.ProfileId);
            return profile?.DisplayName;
        }
        catch
        {
            return null;  // header just falls back to "PiggyBank"
        }
    }

    private static string Escape(string v) =>
        v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    private void BuildMonthlySpendChart(IReadOnlyList<MonthlySpendPoint> points)
    {
        MonthlySpendSeries.Clear();
        MonthlySpendXAxis.Clear();
        MonthlySpendYAxis.Clear();

        MonthlySpendSeries.Add(new LineSeries<decimal>
        {
            Name = "Monthly spend",
            Values = points.Select(p => p.Total).ToArray(),
            GeometrySize = 6,
        });
        MonthlySpendXAxis.Add(new Axis
        {
            Labels = points.Select(p => p.Label).ToArray(),
            LabelsRotation = 15,
        });
        MonthlySpendYAxis.Add(new Axis
        {
            Labeler = v => $"£{v:N0}",
            MinLimit = 0,
        });
    }

    private void BuildCategoryChart(IReadOnlyList<CategorySpendPoint> points)
    {
        CategorySpendSeries.Clear();
        CategorySpendXAxis.Clear();
        CategorySpendYAxis.Clear();

        var top = points.Take(10).ToList();
        var other = points.Skip(10).Sum(p => p.Total);
        if (other > 0m) top.Add(new CategorySpendPoint("Other", other));

        CategorySpendSeries.Add(new ColumnSeries<decimal>
        {
            Name = "Spend",
            Values = top.Select(p => p.Total).ToArray(),
        });
        CategorySpendXAxis.Add(new Axis
        {
            Labels = top.Select(p => p.CategoryName).ToArray(),
            LabelsRotation = 25,
        });
        CategorySpendYAxis.Add(new Axis
        {
            Labeler = v => $"£{v:N0}",
            MinLimit = 0,
        });
    }
}

public sealed record MonthSummaryRow(Guid MonthId, string PeriodLabel, string Status, decimal NetSpend);
