using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Debts;

public sealed partial class DebtsViewModel(
    IProfileSessionManager sessions,
    BudgetCalculator calc,
    TimeProvider clock) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly BudgetCalculator _calc = calc;
    private readonly TimeProvider _clock = clock;

    public ObservableCollection<DebtRow> Debts { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasDebts;
    [ObservableProperty] private decimal _totalOwed;
    [ObservableProperty] private decimal _totalShortTermOwed;
    [ObservableProperty] private decimal _totalLongTermOwed;
    [ObservableProperty] private decimal _combinedUtilisation;

    // --- Payoff simulator ---
    [ObservableProperty] private decimal _extraMonthly;
    [ObservableProperty] private string _baselineSummary = "Add a debt to see the simulator.";
    [ObservableProperty] private string _avalancheSummary = "";
    [ObservableProperty] private string _snowballSummary = "";
    [ObservableProperty] private string _avalancheDelta = "";
    [ObservableProperty] private string _snowballDelta = "";
    [ObservableProperty] private string _avalancheOrder = "";
    [ObservableProperty] private string _snowballOrder = "";
    [ObservableProperty] private bool _avalancheRecommended;
    [ObservableProperty] private bool _snowballRecommended;
    [ObservableProperty] private bool _hasSimulation;

    // --- Add-debt form ---
    [ObservableProperty] private string _newDebtName = "";
    [ObservableProperty] private DebtKindOption _newDebtKind = DebtKindOption.All[0];
    [ObservableProperty] private decimal? _newDebtLimit;
    [ObservableProperty] private decimal _newDebtOpeningBalance;
    [ObservableProperty] private decimal? _newDebtAprPercent;
    [ObservableProperty] private decimal? _newDebtMinimumPayment;
    [ObservableProperty] private decimal? _newDebtScheduledOverpayment;
    [ObservableProperty] private int? _newDebtOverpaymentPenaltyDays;

    public IReadOnlyList<DebtKindOption> DebtKinds { get; } = DebtKindOption.All;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
            var list = await repo.ListWithLatestBalancesAsync(ct: ct);

            foreach (var row in Debts) row.PropertyChanged -= OnRowPropertyChanged;
            Debts.Clear();
            // Display order: group by Kind (shown as group headers), within each
            // group show debts with a non-zero balance first (sorted by APR desc
            // — highest-interest pain at the top), then zero-balance rows last
            // so cleared-but-kept cards stay visible without cluttering the top.
            var ordered = list
                .OrderBy(d => (int)d.Debt.Kind)
                .ThenBy(d => d.LatestBalance == 0m)
                .ThenByDescending(d => d.Debt.AnnualPercentageRate ?? 0m)
                .ThenByDescending(d => Math.Abs(d.LatestBalance));
            foreach (var item in ordered)
            {
                var row = new DebtRow(item);
                row.PropertyChanged += OnRowPropertyChanged;
                Debts.Add(row);
            }

            HasDebts = Debts.Count > 0;
            Recalculate();
            // Run the sim on load so the cards show numbers as soon as the
            // user lands on Debts. Auto-recompute then keeps them current
            // through every inline edit.
            RunSimulation();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAddDebt))]
    public async Task AddDebtAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
        await repo.AddAsync(new Debt
        {
            Name = NewDebtName.Trim(),
            Kind = NewDebtKind.Kind,
            Limit = NewDebtLimit,
            AnnualPercentageRate = NewDebtAprPercent is null ? null : NewDebtAprPercent.Value / 100m,
            MinimumMonthlyPayment = NewDebtMinimumPayment,
            ScheduledOverpayment = NewDebtScheduledOverpayment,
            OverpaymentInterestPenaltyDays = NewDebtOverpaymentPenaltyDays,
            OpeningBalance = -Math.Abs(NewDebtOpeningBalance),  // debt is owed → always negative
            SortOrder = Debts.Count,
        }, ct);

        NewDebtName = "";
        NewDebtLimit = null;
        NewDebtOpeningBalance = 0m;
        NewDebtAprPercent = null;
        NewDebtMinimumPayment = null;
        NewDebtScheduledOverpayment = null;
        NewDebtOverpaymentPenaltyDays = null;
        await LoadAsync(ct);
    }

    // Name is the only hard requirement. A zero-balance card with a limit
    // legitimately reduces overall credit utilisation, so "Owed = 0" must
    // be addable; loans with a known limit but no current balance are
    // similarly valid.
    private bool CanAddDebt() => !string.IsNullOrWhiteSpace(NewDebtName);

    partial void OnNewDebtNameChanged(string value) => AddDebtCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task AddSnapshotAsync(DebtRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
        await repo.AddSnapshotAsync(new DebtSnapshot
        {
            DebtId = row.Id,
            SnapshotDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime),
            Balance = -Math.Abs(row.NewSnapshotBalance),
        }, ct);
        row.NewSnapshotBalance = 0m;
        await LoadAsync(ct);
    }

    [RelayCommand]
    public async Task ArchiveDebtAsync(DebtRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Archive \"{row.Name}\"? Snapshot history is kept on record but it won't appear in active totals.",
            "Archive debt",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
        await repo.ArchiveAsync(row.Id, ct);
        await LoadAsync(ct);
    }

    private async void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DebtRow row) return;
        // NewSnapshotBalance is just a transient input box — don't save or recalc.
        if (e.PropertyName is nameof(DebtRow.NewSnapshotBalance)) return;

        if (e.PropertyName is nameof(DebtRow.Name)
                           or nameof(DebtRow.Limit)
                           or nameof(DebtRow.AprPercent)
                           or nameof(DebtRow.MinimumPayment)
                           or nameof(DebtRow.ScheduledOverpayment)
                           or nameof(DebtRow.OverpaymentPenaltyDays))
        {
            // Inline-save on edit keeps the summary totals truthful.
            if (_sessions.Current is null) return;
            var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
            var entity = await repo.FindAsync(row.Id);
            if (entity is not null)
            {
                entity.Name = row.Name;
                entity.Limit = row.Limit;
                entity.AnnualPercentageRate = row.AprPercent is null ? null : row.AprPercent.Value / 100m;
                entity.MinimumMonthlyPayment = row.MinimumPayment;
                entity.ScheduledOverpayment = row.ScheduledOverpayment;
                entity.OverpaymentInterestPenaltyDays = row.OverpaymentPenaltyDays;
                await repo.UpdateAsync(entity);
            }
        }
        else if (e.PropertyName is nameof(DebtRow.BalanceInput))
        {
            await HandleBalanceEditAsync(row);
        }
        Recalculate();
        // Any input that affects the simulation re-runs it. Keeps the
        // summary cards honest after an inline edit instead of waiting
        // on the user to click Run again.
        if (e.PropertyName is nameof(DebtRow.AprPercent)
                           or nameof(DebtRow.MinimumPayment)
                           or nameof(DebtRow.ScheduledOverpayment)
                           or nameof(DebtRow.OverpaymentPenaltyDays)
                           or nameof(DebtRow.BalanceInput)
                           or nameof(DebtRow.LatestBalance))
        {
            RunSimulation();
        }
    }

    /// <summary>Re-run the simulator when the user edits the global
    /// Extra Monthly field. Same logic as per-row edits — keep the cards
    /// in sync without a button click.</summary>
    partial void OnExtraMonthlyChanged(decimal value) => RunSimulation();

    /// <summary>Turn the row's BalanceInput into a new snapshot. Zero prompts
    /// for confirmation because "paid off" is a meaningful product event;
    /// non-zero edits just write the snapshot silently.</summary>
    private async Task HandleBalanceEditAsync(DebtRow row)
    {
        if (_sessions.Current is null) return;
        // Skip no-op commits (same value as current).
        if (row.BalanceInput == Math.Abs(row.LatestBalance)) return;

        if (row.BalanceInput == 0m)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Mark \"{row.Name}\" as paid off? A £0 snapshot is recorded; you can always add a new balance later.",
                "Confirm zero balance",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.OK)
            {
                // Revert the edit without re-entering this handler.
                row.PropertyChanged -= OnRowPropertyChanged;
                row.BalanceInput = Math.Abs(row.LatestBalance);
                row.PropertyChanged += OnRowPropertyChanged;
                return;
            }
        }

        var repo = _sessions.Current.Services.GetRequiredService<IDebtRepository>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        await repo.AddSnapshotAsync(new DebtSnapshot
        {
            DebtId = row.Id,
            SnapshotDate = today,
            Balance = -row.BalanceInput,  // debts stored negative
        });

        // Sync derived fields so the display + utilisation update without a reload.
        row.LatestBalance = -row.BalanceInput;
        row.LatestSnapshotDate = today;
    }

    [RelayCommand]
    public void RunSimulation()
    {
        // Only short-term debts — mortgage simulation would dominate and
        // isn't what people mean by "snowball". Could be a checkbox later.
        var inputs = Debts
            .Where(d => d.Kind != DebtKind.Mortgage && Math.Abs(d.LatestBalance) > 0m)
            .Select(d =>
            {
                var balance = Math.Abs(d.LatestBalance);
                // Fall back to a reasonable default only when the user hasn't
                // set a minimum. Real values (set inline on the row or the
                // add form) always win.
                var min = d.MinimumPayment ?? (d.Kind == DebtKind.CreditCard
                    ? Math.Max(5m, Math.Round(balance * 0.02m, 2))  // 2% of balance for cards
                    : Math.Max(5m, Math.Round(balance / 36m, 2)));  // 3-year heuristic for fixed loans
                var overpay = d.ScheduledOverpayment ?? 0m;
                return new PiggyBank.Core.Budgeting.PayoffDebt(
                    d.Name,
                    balance,
                    (d.AprPercent ?? 0m) / 100m,
                    min,
                    overpay,
                    d.OverpaymentPenaltyDays);
            })
            .ToList();

        if (inputs.Count == 0)
        {
            BaselineSummary = "No short-term debts with a balance — nothing to simulate.";
            AvalancheSummary = "";
            SnowballSummary = "";
            AvalancheDelta = "";
            SnowballDelta = "";
            AvalancheOrder = "";
            SnowballOrder = "";
            AvalancheRecommended = false;
            SnowballRecommended = false;
            HasSimulation = false;
            return;
        }

        var sim = new PiggyBank.Core.Budgeting.DebtPayoffSimulator();

        // Baseline: minimums only — strip out every scheduled overpayment and
        // don't apply the extra budget. The strategy doesn't matter when there's
        // nothing to direct (Avalanche == Snowball == "pay minimums").
        var baselineInputs = inputs
            .Select(d => d with { ScheduledOverpayment = 0m })
            .ToList();
        var baseline = sim.RunAvalanche(baselineInputs, extraMonthly: 0m);

        var avalanche = sim.RunAvalanche(inputs, ExtraMonthly);
        var snowball = sim.RunSnowball(inputs, ExtraMonthly);

        BaselineSummary = FormatPlan(baseline);
        AvalancheSummary = FormatPlan(avalanche);
        SnowballSummary = FormatPlan(snowball);
        AvalancheDelta = FormatDelta(baseline, avalanche);
        SnowballDelta = FormatDelta(baseline, snowball);

        // Priority order: top 3 debts by each strategy's rule. Avalanche
        // ranks by APR descending; snowball ranks by balance ascending.
        // Same input set the simulator used so the labels match what the
        // sim actually attacks.
        AvalancheOrder = FormatPriority(inputs.OrderByDescending(d => d.AprFraction));
        SnowballOrder = FormatPriority(inputs.OrderBy(d => d.Balance));

        // Recommended = clears in fewer months OR (tie) less total interest.
        // When they tie completely (e.g. one debt only) neither gets the
        // badge — the user picks based on their own preference.
        if (avalanche.MonthsToClear < snowball.MonthsToClear
            || (avalanche.MonthsToClear == snowball.MonthsToClear
                && avalanche.TotalInterestPaid < snowball.TotalInterestPaid))
        {
            AvalancheRecommended = true;
            SnowballRecommended = false;
        }
        else if (snowball.MonthsToClear < avalanche.MonthsToClear
                 || snowball.TotalInterestPaid < avalanche.TotalInterestPaid)
        {
            AvalancheRecommended = false;
            SnowballRecommended = true;
        }
        else
        {
            AvalancheRecommended = false;
            SnowballRecommended = false;
        }

        HasSimulation = true;
    }

    private static string FormatPlan(PiggyBank.Core.Budgeting.DebtPayoffPlan plan)
    {
        if (plan.MonthsToClear >= PiggyBank.Core.Budgeting.DebtPayoffSimulator.MaxMonths)
            return "Won't clear within 50 years — increase the extra monthly.";
        return $"{Span(plan.MonthsToClear)} to clear · £{plan.TotalInterestPaid:N2} interest";
    }

    /// <summary>"saves £X + Yy Zm vs baseline" suffix — the headline value
    /// of overpaying. Empty when baseline didn't clear (the saving is
    /// "you finish ever" which doesn't render as money).</summary>
    private static string FormatDelta(
        PiggyBank.Core.Budgeting.DebtPayoffPlan baseline,
        PiggyBank.Core.Budgeting.DebtPayoffPlan strategy)
    {
        var max = PiggyBank.Core.Budgeting.DebtPayoffSimulator.MaxMonths;
        if (baseline.MonthsToClear >= max || strategy.MonthsToClear >= max) return "";
        var monthsSaved = baseline.MonthsToClear - strategy.MonthsToClear;
        var interestSaved = baseline.TotalInterestPaid - strategy.TotalInterestPaid;
        if (monthsSaved <= 0 && interestSaved <= 0m) return "";
        return $"saves £{interestSaved:N2} + {Span(monthsSaved)} vs baseline";
    }

    private static string FormatPriority(IEnumerable<PiggyBank.Core.Budgeting.PayoffDebt> ordered)
    {
        var top = ordered.Take(3).Select(d => d.Name).ToArray();
        if (top.Length == 0) return "";
        return "target order: " + string.Join(" → ", top);
    }

    private static string Span(int months)
    {
        var years = months / 12;
        var rem = months % 12;
        return years > 0 ? $"{years}y {rem}m" : $"{rem}m";
    }

    private void Recalculate()
    {
        // Mortgage dwarfs every other debt and confuses at-a-glance health
        // checks, so split it out. Short-term = everything except Mortgage;
        // Long-term = Mortgage only.
        var (shortTerm, longTerm) = (0m, 0m);
        foreach (var d in Debts)
        {
            var abs = Math.Abs(d.LatestBalance);
            if (d.Kind == DebtKind.Mortgage) longTerm += abs;
            else shortTerm += abs;
        }
        TotalShortTermOwed = shortTerm;
        TotalLongTermOwed = longTerm;
        TotalOwed = shortTerm + longTerm;

        // Credit utilisation is a revolving-credit concept (credit cards). Loans
        // and mortgages have balances but no "limit to draw against", so including
        // their balances in the numerator while omitting their (non-existent)
        // limits from the denominator inflates the ratio wildly. Filter to rows
        // that actually carry a limit.
        CombinedUtilisation = _calc.DebtUtilisation(
            Debts.Where(d => d.Limit is > 0m)
                 .Select(d => (d.LatestBalance, d.Limit!.Value)));
    }
}

public sealed partial class DebtRow : ObservableObject
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public Guid Id { get; }
    public DebtKind Kind { get; }
    public string KindLabel { get; }
    /// <summary>Only credit cards have a limit — hides the box for loans etc.</summary>
    public bool HasLimit { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private decimal? _limit;
    [ObservableProperty] private decimal? _aprPercent;
    [ObservableProperty] private decimal? _minimumPayment;
    [ObservableProperty] private decimal? _scheduledOverpayment;
    [ObservableProperty] private int? _overpaymentPenaltyDays;
    [ObservableProperty] private decimal _latestBalance;
    [ObservableProperty] private DateOnly? _latestSnapshotDate;
    [ObservableProperty] private string _latestBalanceDisplay;
    [ObservableProperty] private string _utilisationDisplay;
    /// <summary>Editable balance displayed as a positive magnitude (owed £X).
    /// VM persists a snapshot on change; UI stays one-field simple.</summary>
    [ObservableProperty] private decimal _balanceInput;
    [ObservableProperty] private decimal _newSnapshotBalance;

    public DebtRow(DebtWithLatestBalance source)
    {
        Id = source.Debt.Id;
        Kind = source.Debt.Kind;
        _name = source.Debt.Name;
        _limit = source.Debt.Limit;
        _aprPercent = source.Debt.AnnualPercentageRate is null ? null : source.Debt.AnnualPercentageRate * 100m;
        _minimumPayment = source.Debt.MinimumMonthlyPayment;
        _scheduledOverpayment = source.Debt.ScheduledOverpayment;
        _overpaymentPenaltyDays = source.Debt.OverpaymentInterestPenaltyDays;
        _latestBalance = source.LatestBalance;
        _latestSnapshotDate = source.LatestSnapshotDate;
        KindLabel = DebtKindOption.HumaniseKind(source.Debt.Kind);
        HasLimit = source.Debt.Kind is DebtKind.CreditCard;
        _latestBalanceDisplay = FormatBalance(source.LatestBalance);
        _utilisationDisplay = FormatUtilisation(source.LatestBalance, source.Debt.Limit);
        _balanceInput = Math.Abs(source.LatestBalance);
    }

    partial void OnLatestBalanceChanged(decimal value)
    {
        LatestBalanceDisplay = FormatBalance(value);
        UtilisationDisplay = FormatUtilisation(value, Limit);
    }

    partial void OnLimitChanged(decimal? value)
    {
        UtilisationDisplay = FormatUtilisation(LatestBalance, value);
    }

    private static string FormatBalance(decimal balance) =>
        balance.ToString("C2", EnGb);

    private static string FormatUtilisation(decimal balance, decimal? limit)
    {
        if (limit is null || limit == 0m) return "—";
        var ratio = Math.Abs(balance) / limit.Value;
        return ratio.ToString("P0", EnGb);
    }
}

/// <summary>ComboBox item that pairs a <see cref="DebtKind"/> enum value
/// with a readable label ("Credit card", "Car / asset finance", etc).</summary>
public sealed record DebtKindOption(DebtKind Kind, string Label)
{
    public static IReadOnlyList<DebtKindOption> All { get; } =
    [
        new(DebtKind.CreditCard, "Credit card"),
        new(DebtKind.Loan,       "Loan"),
        new(DebtKind.Finance,    "Car / asset finance"),
        new(DebtKind.Mortgage,   "Mortgage"),
        new(DebtKind.Other,      "Other"),
    ];

    public static string HumaniseKind(DebtKind kind) =>
        All.FirstOrDefault(o => o.Kind == kind)?.Label ?? kind.ToString();

    public override string ToString() => Label;
}
