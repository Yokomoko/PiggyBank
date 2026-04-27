using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Core.Payday;
using PiggyBank.Data;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;

namespace PiggyBank.App.Dashboard;

public sealed partial class CurrentMonthViewModel(
    IProfileSessionManager sessions,
    TimeProvider clock,
    BudgetCalculator calc) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly TimeProvider _clock = clock;
    private readonly BudgetCalculator _calc = calc;

    private ProfileSettings? _profileSettings;

    public ObservableCollection<MonthlyOutgoingRow> Outgoings { get; } = [];
    public ObservableCollection<TransactionRow> Transactions { get; } = [];
    public ObservableCollection<CategoryRollupRow> CategoryRollups { get; } = [];
    public ObservableCollection<Category> AvailableCategories { get; } = [];

    [ObservableProperty] private Month? _month;
    [ObservableProperty] private string _periodLabel = "";
    [ObservableProperty] private bool _isBusy;
    /// <summary>True when the salary is shown; false renders "••••••"
    /// in the header for shoulder-surf privacy.</summary>
    [ObservableProperty] private bool _isSalaryVisible;
    [ObservableProperty] private string _salaryDisplay = "••••••";
    [ObservableProperty] private string _salaryToggleLabel = "Show salary";
    [ObservableProperty] private bool _needsMonthCreated;

    /// <summary>Non-null only when a prior month is closed AND its closing balance
    /// hasn't yet been applied as this month's carry-over.</summary>
    [ObservableProperty] private decimal? _suggestedRollover;
    [ObservableProperty] private bool _rolloverBannerVisible;

    // --- Editable month header fields ---
    [ObservableProperty] private DateOnly _lastPayday;
    [ObservableProperty] private DateOnly _nextPayday;
    [ObservableProperty] private decimal _carriedOverBalance;
    [ObservableProperty] private decimal? _monthlySalary;
    [ObservableProperty] private bool _isClosed;

    // --- Allowance engine surface ---
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private decimal _monthlySpendTotal;
    [ObservableProperty] private decimal _grandTotal;
    [ObservableProperty] private decimal _daysToNextPayday;
    [ObservableProperty] private decimal _daysSincePayday;
    [ObservableProperty] private decimal _allowedSpendPerDay;
    [ObservableProperty] private decimal _allowedMonthlyRemaining;
    [ObservableProperty] private decimal _allowedWeeklyRemaining;
    [ObservableProperty] private decimal _spentPerDayToDate;
    [ObservableProperty] private decimal _estimatedSpend;
    [ObservableProperty] private decimal _overUnder;
    [ObservableProperty] private decimal _extraSpendToSave;

    // --- Savings outlook (derived from OverUnder) ---
    [ObservableProperty] private string _savingsHeadline = "SAVINGS OUTLOOK";
    [ObservableProperty] private string _savingsCaption = "Record a deposit once this month closes.";
    [ObservableProperty] private decimal _projectedSavings;
    [ObservableProperty] private bool _isSavingsPositive;

    public event EventHandler<decimal>? RecordSavingsRequested;

    // --- Quick-add spend form ---
    [ObservableProperty] private DateOnly _newSpendDate;
    [ObservableProperty] private string _newSpendPayee = "";
    [ObservableProperty] private decimal _newSpendAmount;
    [ObservableProperty] private Category? _newSpendCategory;

    // --- Quick-add outgoing form ---
    [ObservableProperty] private string _newOutgoingName = "";
    [ObservableProperty] private decimal _newOutgoingAmount;
    [ObservableProperty] private Category? _newOutgoingCategory;
    [ObservableProperty] private bool _newOutgoingIsAllocation;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            var scope = _sessions.Current.Services;
            var months = scope.GetRequiredService<IMonthRepository>();
            var outgoings = scope.GetRequiredService<IMonthlyOutgoingRepository>();
            var txs = scope.GetRequiredService<ITransactionRepository>();
            var cats = scope.GetRequiredService<ICategoryRepository>();
            var db = scope.GetRequiredService<AppDbContext>();

            _profileSettings = await db.ProfileSettings.FirstOrDefaultAsync(ct);

            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var month = await months.FindForDateAsync(today, ct) ?? await months.FindOpenAsync(ct);

            NeedsMonthCreated = month is null;
            if (month is null)
            {
                Clear();
                NewSpendDate = today;
                return;
            }

            Month = month;
            PeriodLabel = $"{month.PeriodStart:dd MMM yyyy} – {month.PeriodEnd:dd MMM yyyy}";
            LastPayday = month.LastPayday;
            NextPayday = month.NextPayday;
            CarriedOverBalance = month.CarriedOverBalance;
            MonthlySalary = month.MonthlySalary;
            RefreshSalaryDisplay();
            IsClosed = month.IsClosed;
            NewSpendDate = today;

            // Detach PropertyChanged handlers before Clear() — otherwise the row
            // objects stay subscribed and a stale change during dispose leaks
            // updates into the repository for a row no longer in the grid.
            foreach (var row in Outgoings) row.PropertyChanged -= OutgoingRowChanged;
            Outgoings.Clear();
            foreach (var o in await outgoings.ListForMonthAsync(month.Id, ct))
                Outgoings.Add(WireOutgoing(new MonthlyOutgoingRow(o), IsSalaryVisible));

            foreach (var row in Transactions) row.PropertyChanged -= TransactionRowChanged;
            Transactions.Clear();
            foreach (var t in await txs.ListForMonthAsync(month.Id, ct))
                Transactions.Add(WireTransaction(new TransactionRow(t)));

            // Ledger only shows allocation-flagged categories (per user direction).
            await RefreshLedgerCategoriesAsync(ct);

            await RefreshRollupsAsync(scope, month.Id, ct);

            var monthSvc = scope.GetRequiredService<MonthService>();
            var suggestion = await monthSvc.SuggestRolloverAsync(month.Id, ct);
            SuggestedRollover = suggestion;
            RolloverBannerVisible = suggestion is not null && month.CarriedOverBalance == 0m;

            Recalculate();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task CreateCurrentMonthAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var svc = scope.GetRequiredService<MonthService>();
        var db = scope.GetRequiredService<AppDbContext>();

        var settings = await db.ProfileSettings.FirstOrDefaultAsync(ct) ?? new ProfileSettings();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var (lastPayday, nextPayday) = PaydayCalculator.ResolvePayWindow(
            today,
            settings.PrimaryPaydayDayOfMonth,
            settings.AdjustPaydayForWeekendsAndBankHolidays);

        await svc.CreateAsync(lastPayday, nextPayday, ct: ct);
        await LoadAsync(ct);
    }

    /// <summary>Close the month. Once closed, the Current Month screen
    /// shows it read-only and any rollover the user wants carries forward
    /// through the banner on the NEXT month. If any allocation has unspent
    /// money left, offers the user a deposit prompt so the surplus can
    /// land in a pocket instead of evaporating.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseMonth))]
    public async Task CloseMonthAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || Month is null) return;
        var svc = _sessions.Current.Services.GetRequiredService<MonthService>();
        await svc.CloseAsync(Month.Id, ct);
        IsClosed = true;
        Month.IsClosed = true;

        // Allocation leftover → offer to record as a deposit.
        // Sum(Allocated - Used) across allocation rows; clamp negatives (overspend).
        var unspent = Outgoings
            .Where(o => o.IsAllocation)
            .Sum(o => Math.Max(0m, Math.Abs(o.Amount) - o.AllocationUsed));
        if (unspent > 0m)
        {
            var gb = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            var confirm = System.Windows.MessageBox.Show(
                $"You have {unspent.ToString("C2", gb)} unspent across your allocations this month. "
                + "Record as a deposit into your pockets?",
                "Month closed — record surplus?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm == System.Windows.MessageBoxResult.Yes)
                RecordSavingsRequested?.Invoke(this, unspent);
        }
    }

    private bool CanCloseMonth() => Month is not null && !IsClosed;

    partial void OnIsClosedChanged(bool value) => CloseMonthCommand.NotifyCanExecuteChanged();
    partial void OnCarriedOverBalanceChanged(decimal value) => Recalculate();

    [RelayCommand]
    public async Task ApplyRolloverAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || Month is null || SuggestedRollover is null) return;
        var svc = _sessions.Current.Services.GetRequiredService<MonthService>();
        await svc.ApplyRolloverFromPriorAsync(Month.Id, ct);
        await LoadAsync(ct);
    }

    [RelayCommand]
    private void DismissRolloverBanner() => RolloverBannerVisible = false;

    [RelayCommand]
    private void ToggleWage()
    {
        IsSalaryVisible = !IsSalaryVisible;
        SalaryToggleLabel = IsSalaryVisible ? "Hide salary" : "Show salary";
        RefreshSalaryDisplay();
        foreach (var row in Outgoings) row.ApplyWageMask(IsSalaryVisible);
    }

    private void RefreshSalaryDisplay()
    {
        SalaryDisplay = IsSalaryVisible
            ? (MonthlySalary ?? 0m).ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))
            : "••••••";
    }

    partial void OnMonthlySalaryChanged(decimal? value)
    {
        Recalculate();
        RefreshSalaryDisplay();
    }

    /// <summary>Persist manual edits to the month header (paydays + rollover).</summary>
    [RelayCommand]
    public async Task SaveMonthHeaderAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || Month is null) return;
        var months = _sessions.Current.Services.GetRequiredService<IMonthRepository>();
        Month.LastPayday = LastPayday;
        Month.NextPayday = NextPayday;
        Month.PeriodStart = LastPayday;
        Month.PeriodEnd = NextPayday.AddDays(-1);
        Month.CarriedOverBalance = CarriedOverBalance;
        Month.MonthlySalary = MonthlySalary;
        await months.UpdateAsync(Month, ct);
        PeriodLabel = $"{Month.PeriodStart:dd MMM yyyy} – {Month.PeriodEnd:dd MMM yyyy}";
        Recalculate();
    }

    [RelayCommand(CanExecute = nameof(CanAddSpend))]
    public async Task AddSpendAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || Month is null) return;
        var txs = _sessions.Current.Services.GetRequiredService<ITransactionRepository>();

        var t = new Transaction
        {
            MonthId = Month.Id,
            Date = NewSpendDate,
            Payee = NewSpendPayee.Trim(),
            Amount = -Math.Abs(NewSpendAmount),  // spends are always stored negative
            CategoryId = NewSpendCategory?.Id,
        };
        await txs.AddAsync(t, ct);

        Transactions.Insert(0, WireTransaction(new TransactionRow(t)));
        await RefreshRollupsAsync(_sessions.Current.Services, Month.Id, ct);
        Recalculate();

        // Reset the form but keep the category (user likely entering several of the same type)
        NewSpendPayee = "";
        NewSpendAmount = 0m;
    }

    private bool CanAddSpend() =>
        Month is not null && !string.IsNullOrWhiteSpace(NewSpendPayee) && NewSpendAmount != 0m;

    partial void OnNewSpendPayeeChanged(string value) => AddSpendCommand.NotifyCanExecuteChanged();
    partial void OnNewSpendAmountChanged(decimal value) => AddSpendCommand.NotifyCanExecuteChanged();
    partial void OnMonthChanged(Month? value) => AddSpendCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task DeleteSpendAsync(TransactionRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null || Month is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete \"{row.Payee}\" on {row.Date:dd MMM}?",
            "Delete transaction",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var txs = _sessions.Current.Services.GetRequiredService<ITransactionRepository>();
        await txs.DeleteAsync(row.Id, ct);
        row.PropertyChanged -= TransactionRowChanged;
        Transactions.Remove(row);
        await RefreshRollupsAsync(_sessions.Current.Services, Month.Id, ct);
        Recalculate();
    }

    [RelayCommand(CanExecute = nameof(CanAddOutgoing))]
    public async Task AddOutgoingAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || Month is null) return;
        var scope = _sessions.Current.Services;
        var outgoings = scope.GetRequiredService<IMonthlyOutgoingRepository>();

        var name = NewOutgoingName.Trim();
        Guid? categoryId = NewOutgoingCategory?.Id;

        // Allocation-tagged outgoings auto-create (or reuse) a matching category
        // so ledger transactions can be tagged to it for drawdown.
        if (NewOutgoingIsAllocation && categoryId is null)
        {
            var cat = await EnsureAllocationCategoryAsync(scope, name, ct);
            categoryId = cat.Id;
            AppendCategoryIfNew(cat);
        }

        var o = new MonthlyOutgoing
        {
            MonthId = Month.Id,
            Name = name,
            Amount = NewOutgoingAmount,
            CategoryId = categoryId,
            IsAllocation = NewOutgoingIsAllocation,
            SortOrder = Outgoings.Count,
        };
        await outgoings.AddAsync(o, ct);

        Outgoings.Add(WireOutgoing(new MonthlyOutgoingRow(o), IsSalaryVisible));
        Recalculate();

        NewOutgoingName = "";
        NewOutgoingAmount = 0m;
        NewOutgoingIsAllocation = false;
    }

    /// <summary>Find or create a Category whose Name matches the outgoing's
    /// name (case-insensitive). Used when flagging an outgoing as an
    /// allocation so the user can tag ledger drawdowns to it.</summary>
    private static async Task<Category> EnsureAllocationCategoryAsync(
        IServiceProvider scope, string outgoingName, CancellationToken ct)
    {
        var cats = scope.GetRequiredService<ICategoryRepository>();
        var existing = (await cats.ListAsync(ct: ct))
            .FirstOrDefault(c => string.Equals(c.Name, outgoingName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        return await cats.AddAsync(outgoingName, CategoryKind.Spend, ct: ct);
    }

    /// <summary>Append <paramref name="cat"/> to <see cref="AvailableCategories"/> if
    /// it's not already present. Critical: we never Clear() the collection at runtime
    /// because any ledger ComboBox bound to it would briefly have no items and
    /// SelectedValue bindings would cascade-null their CategoryId — wiping the user's
    /// selections. Full refresh is only safe during LoadAsync, before rows exist.</summary>
    private void AppendCategoryIfNew(Category cat)
    {
        if (!AvailableCategories.Any(c => c.Id == cat.Id))
            AvailableCategories.Add(cat);
    }

    /// <summary>Rebuilds the <see cref="AvailableCategories"/> collection.
    /// Lists every category so the user can categorise normal spend (Food,
    /// Tesco, etc.); if the chosen category happens to match an allocation
    /// outgoing, drawdown logic kicks in automatically at Recalculate time.</summary>
    private async Task RefreshLedgerCategoriesAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var cats = _sessions.Current.Services.GetRequiredService<ICategoryRepository>();
        var all = await cats.ListAsync(ct: ct);

        AvailableCategories.Clear();
        foreach (var c in all) AvailableCategories.Add(c);
    }

    /// <summary>Public refresh hook for <see cref="CategoryChangeNotifier"/>:
    /// fired when the Settings dialog adds or archives a category, so the
    /// in-progress Current Month view's dropdowns pick up the change without
    /// the user having to navigate away and back.</summary>
    [RelayCommand]
    public async Task RefreshCategoriesAsync(CancellationToken ct = default)
        => await RefreshLedgerCategoriesAsync(ct);

    private bool CanAddOutgoing() =>
        Month is not null && !string.IsNullOrWhiteSpace(NewOutgoingName) && NewOutgoingAmount != 0m;

    partial void OnNewOutgoingNameChanged(string value) => AddOutgoingCommand.NotifyCanExecuteChanged();
    partial void OnNewOutgoingAmountChanged(decimal value) => AddOutgoingCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task DeleteOutgoingAsync(MonthlyOutgoingRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete outgoing \"{row.Name}\"? This only affects the current month.",
            "Delete outgoing",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var outgoings = _sessions.Current.Services.GetRequiredService<IMonthlyOutgoingRepository>();
        await outgoings.DeleteAsync(row.Id, ct);
        row.PropertyChanged -= OutgoingRowChanged;
        Outgoings.Remove(row);
        Recalculate();
    }

    // --- Row-level edit propagation ---

    private MonthlyOutgoingRow WireOutgoing(MonthlyOutgoingRow row, bool wageVisible)
    {
        row.ApplyWageMask(wageVisible);
        row.PropertyChanged += OutgoingRowChanged;
        return row;
    }

    private TransactionRow WireTransaction(TransactionRow row)
    {
        row.PropertyChanged += TransactionRowChanged;
        return row;
    }

    private async void OutgoingRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MonthlyOutgoingRow row) return;
        if (e.PropertyName is not (nameof(MonthlyOutgoingRow.Name)
                                or nameof(MonthlyOutgoingRow.Amount)
                                or nameof(MonthlyOutgoingRow.CategoryId)
                                or nameof(MonthlyOutgoingRow.IsAllocation))) return;

        if (_sessions.Current is null) return;
        var scope = _sessions.Current.Services;
        var repo = scope.GetRequiredService<IMonthlyOutgoingRepository>();
        var entity = await repo.FindAsync(row.Id);
        if (entity is null) return;

        // Flipping ON IsAllocation with no category → auto-create/link category.
        // Append (don't refresh) so existing ledger ComboBoxes keep their selection.
        if (row.IsAllocation && row.CategoryId is null)
        {
            var cat = await EnsureAllocationCategoryAsync(scope, row.Name, default);
            row.CategoryId = cat.Id;
            AppendCategoryIfNew(cat);
        }

        entity.Name = row.Name;
        entity.Amount = row.Amount;
        entity.CategoryId = row.CategoryId;
        entity.IsAllocation = row.IsAllocation;
        await repo.UpdateAsync(entity);

        Recalculate();
    }

    private async void TransactionRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TransactionRow row) return;
        if (e.PropertyName is not (nameof(TransactionRow.Date)
                                or nameof(TransactionRow.Payee)
                                or nameof(TransactionRow.Amount)
                                or nameof(TransactionRow.CategoryId)
                                )) return;

        if (_sessions.Current is null || Month is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<ITransactionRepository>();
        var entity = await repo.FindAsync(row.Id);
        if (entity is null) return;
        entity.Date = row.Date;
        entity.Payee = row.Payee;
        entity.Amount = row.Amount;
        entity.CategoryId = row.CategoryId;
        await repo.UpdateAsync(entity);
        await RefreshRollupsAsync(_sessions.Current.Services, Month.Id);
        Recalculate();
    }

    private async Task RefreshRollupsAsync(IServiceProvider scope, Guid monthId, CancellationToken ct = default)
    {
        var txs = scope.GetRequiredService<ITransactionRepository>();
        var rollups = await txs.SumByCategoryForMonthAsync(monthId, ct);
        CategoryRollups.Clear();
        foreach (var r in rollups)
            CategoryRollups.Add(new CategoryRollupRow(r.CategoryName ?? "Uncategorised", r.Total));
    }

    public void Recalculate()
    {
        if (Month is null) return;

        // Allocation drawdown / overspend math lives in PiggyBank.Core so it's
        // testable without WPF — see AllocationMath + AllocationMathTests.
        // Project the VM rows back into entity shapes so the pure function can
        // do its job; we apply the per-row Used figure to the rows afterwards.
        var monthId = Month.Id;
        var profileId = _sessions.Current?.ProfileId ?? Guid.Empty;
        var outgoingEntities = Outgoings.Select(o => new MonthlyOutgoing
        {
            Id = o.Id,
            ProfileId = profileId,
            MonthId = monthId,
            Name = o.Name,
            Amount = o.Amount,
            CategoryId = o.CategoryId,
            IsAllocation = o.IsAllocation,
            IsWage = o.IsWage,
        }).ToList();
        var transactionEntities = Transactions.Select(t => new Transaction
        {
            Id = t.Id,
            ProfileId = profileId,
            MonthId = monthId,
            Date = t.Date,
            Payee = t.Payee,
            Amount = t.Amount,
            CategoryId = t.CategoryId,
        }).ToList();

        var allocation = AllocationMath.Compute(outgoingEntities, transactionEntities);
        var detailsById = allocation.Details.ToDictionary(d => d.OutgoingId);
        foreach (var row in Outgoings)
        {
            // Non-allocation rows get UpdateAllocationState(0m) — the row's own
            // logic skips the work but still resets any stale state. Allocation
            // rows get the freshly-computed Used so the row can derive its own
            // Remaining / IsOverspent / summary text consistently.
            var used = detailsById.TryGetValue(row.Id, out var d) ? d.Used : 0m;
            row.UpdateAllocationState(used);
        }

        Total = (MonthlySalary ?? 0m) + allocation.TotalOutgoings;
        MonthlySpendTotal = allocation.MonthlySpendTotal;
        // BudgetCalculator's API takes "spend-so-far" as a positive magnitude
        // (per its docs + tests). Our ledger stores spending as negative and
        // income as positive, so flip the sign at the boundary. Without this,
        // spending made MonthlySpendTotal negative → GrandTotal grew instead
        // of shrinking, and SpentPerDayToDate clamped to 0 → EstimatedSpend 0
        // → "projected to save" inflated to the full remaining allowance.
        var spendMagnitude = -MonthlySpendTotal;
        GrandTotal = _calc.GrandTotal(Total, spendMagnitude, CarriedOverBalance);

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        DaysToNextPayday = Math.Max(0, (decimal)(NextPayday.DayNumber - today.DayNumber));
        DaysSincePayday = Math.Max(0, (decimal)(today.DayNumber - LastPayday.DayNumber));

        var buffer = _profileSettings?.BufferPerDay ?? 10m;
        var inputs = new BudgetInputs(GrandTotal, spendMagnitude, DaysSincePayday, DaysToNextPayday, buffer);
        AllowedSpendPerDay = _calc.AllowedSpendPerDay(inputs);
        AllowedMonthlyRemaining = _calc.AllowedMonthlyRemaining(inputs);
        AllowedWeeklyRemaining = _calc.AllowedWeeklyRemaining(inputs);
        SpentPerDayToDate = _calc.SpentPerDayToDate(inputs);
        EstimatedSpend = _calc.EstimatedSpend(inputs);
        OverUnder = _calc.OverUnder(inputs);
        ExtraSpendToSave = _calc.ExtraSpendToSave(inputs);

        // OverUnder = EstimatedSpend − AllowedRemaining:
        //   OverUnder < 0 → projected UNDERspend (money left = potential savings)
        //   OverUnder > 0 → projected OVERspend (shortfall)
        IsSavingsPositive = OverUnder < 0m;
        ProjectedSavings = Math.Abs(OverUnder);
        if (IsSavingsPositive)
        {
            SavingsHeadline = "PROJECTED TO SAVE";
            SavingsCaption = DaysToNextPayday > 0
                ? $"If spending stays on trend for the next {DaysToNextPayday:0} day(s), you'll finish the month with this unspent."
                : "On trend to finish the month with this unspent.";
        }
        else if (OverUnder > 0m)
        {
            SavingsHeadline = "PROJECTED SHORTFALL";
            SavingsCaption = "Current trend finishes the month over budget — ease off daily spend to pull back.";
        }
        else
        {
            SavingsHeadline = "TRACKING TO BUDGET";
            SavingsCaption = "Projected to land at £0 by next payday.";
        }
        RecordSavingsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRecordSavings))]
    private void RecordSavings() => RecordSavingsRequested?.Invoke(this, ProjectedSavings);

    private bool CanRecordSavings() => IsSavingsPositive && ProjectedSavings > 0m;

    private void Clear()
    {
        Month = null;
        PeriodLabel = "";
        foreach (var row in Outgoings) row.PropertyChanged -= OutgoingRowChanged;
        Outgoings.Clear();
        foreach (var row in Transactions) row.PropertyChanged -= TransactionRowChanged;
        Transactions.Clear();
        CategoryRollups.Clear();
        AvailableCategories.Clear();
        Total = MonthlySpendTotal = GrandTotal = 0m;
        AllowedSpendPerDay = AllowedMonthlyRemaining = AllowedWeeklyRemaining = 0m;
        SpentPerDayToDate = EstimatedSpend = OverUnder = ExtraSpendToSave = 0m;
        LastPayday = NextPayday = default;
        CarriedOverBalance = 0m;
        MonthlySalary = null;
    }
}

public sealed partial class MonthlyOutgoingRow(MonthlyOutgoing source) : ObservableObject
{
    public Guid Id { get; } = source.Id;
    public bool IsWage { get; } = source.IsWage;

    [ObservableProperty] private string _name = source.Name;
    [ObservableProperty] private decimal _amount = source.Amount;
    [ObservableProperty] private Guid? _categoryId = source.CategoryId;
    [ObservableProperty] private bool _isAllocation = source.IsAllocation;

    // --- Drawdown state (set by VM.Recalculate for allocation rows) ---
    [ObservableProperty] private decimal _allocationUsed;
    [ObservableProperty] private decimal _allocationRemaining;
    [ObservableProperty] private bool _isOverspent;
    [ObservableProperty] private string _allocationSummary = "";

    // --- Wage privacy ---
    [ObservableProperty] private string _displayAmount = source.Amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    [ObservableProperty] private bool _isMasked;

    public void ApplyWageMask(bool wageVisible)
    {
        IsMasked = IsWage && !wageVisible;
        DisplayAmount = IsMasked
            ? "••••"
            : Amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    }

    /// <summary>Called by the VM during Recalculate with the sum of ledger
    /// drawdowns tagged to this outgoing's category. No-op for non-allocation rows.</summary>
    public void UpdateAllocationState(decimal used)
    {
        var gb = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        if (!IsAllocation)
        {
            AllocationUsed = 0m;
            AllocationRemaining = 0m;
            IsOverspent = false;
            AllocationSummary = "";
            return;
        }
        var allocated = Math.Abs(Amount);
        AllocationUsed = used;
        AllocationRemaining = allocated - used;
        IsOverspent = used > allocated;
        AllocationSummary = IsOverspent
            ? $"Used {used.ToString("C2", gb)} · Over by {(used - allocated).ToString("C2", gb)}"
            : $"Used {used.ToString("C2", gb)} · {AllocationRemaining.ToString("C2", gb)} left";
    }

    partial void OnAmountChanged(decimal value)
    {
        var wageCurrentlyVisible = !IsMasked;
        ApplyWageMask(wageCurrentlyVisible);
    }
}

public sealed partial class TransactionRow(Transaction source) : ObservableObject
{
    public Guid Id { get; } = source.Id;

    [ObservableProperty] private DateOnly _date = source.Date;
    [ObservableProperty] private string _payee = source.Payee;
    [ObservableProperty] private decimal _amount = source.Amount;
    [ObservableProperty] private Guid? _categoryId = source.CategoryId;
}

public sealed record CategoryRollupRow(string Name, decimal Total);
