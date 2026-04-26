using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Pockets;

public sealed partial class PocketsViewModel(
    IProfileSessionManager sessions,
    TimeProvider clock) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly TimeProvider _clock = clock;

    public ObservableCollection<PocketRow> Pockets { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasPockets;
    [ObservableProperty] private decimal _totalBalance;
    [ObservableProperty] private decimal _projectedAnnualInterest;
    [ObservableProperty] private decimal _autoSaveSum;
    [ObservableProperty] private bool _autoSaveWarn;
    [ObservableProperty] private string _autoSaveWarning = "";


    // --- Add-pocket form ---
    [ObservableProperty] private string _newPocketName = "";
    [ObservableProperty] private decimal _newPocketCurrentBalance;
    [ObservableProperty] private decimal _newPocketAutoSave;
    [ObservableProperty] private decimal? _newPocketGoal;
    [ObservableProperty] private decimal _newPocketAnnualRatePercent;
    [ObservableProperty] private DateOnly? _newPocketTargetDate;

    /// <summary>Raised when the user asks to record a new deposit. The
    /// view hosts the modal so the VM stays testable.</summary>
    public event EventHandler? RecordDepositRequested;

    /// <summary>Raised when the user clicks "Deposit" on a specific pocket
    /// row — a direct-to-pocket deposit, bypassing autosave distribution.</summary>
    public event EventHandler<PocketRow>? DepositToPocketRequested;

    /// <summary>Raised when the user asks to archive a pocket that still
    /// has a non-zero balance. The view hosts the modal that lets the user
    /// transfer the balance to another pocket or explicitly write it off.</summary>
    public event EventHandler<PocketRow>? ArchiveTransferRequested;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            var repo = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
            var list = await repo.ListAsync(ct: ct);

            foreach (var row in Pockets) row.PropertyChanged -= OnRowChanged;
            Pockets.Clear();
            // Display order: target-dated pockets first (soonest target first),
            // then undated pockets by AutoSave % descending. Matches how users
            // mentally scan a list of goals.
            var ordered = list
                .OrderBy(p => p.TargetDate is null)           // false (has target) first
                .ThenBy(p => p.TargetDate ?? DateOnly.MaxValue)
                .ThenByDescending(p => p.AutoSavePercent)
                .ThenBy(p => p.Name);
            foreach (var p in ordered)
            {
                var row = new PocketRow(p);
                row.PropertyChanged += OnRowChanged;
                Pockets.Add(row);
            }
            HasPockets = Pockets.Count > 0;
            Recalculate();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAddPocket))]
    public async Task AddPocketAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        await repo.AddAsync(new Pocket
        {
            Name = NewPocketName.Trim(),
            CurrentBalance = NewPocketCurrentBalance,
            AutoSavePercent = NewPocketAutoSave,
            Goal = NewPocketGoal,
            AnnualInterestRate = NewPocketAnnualRatePercent / 100m,
            TargetDate = NewPocketTargetDate,
            SortOrder = Pockets.Count,
        }, ct);

        NewPocketName = "";
        NewPocketCurrentBalance = 0m;
        NewPocketAutoSave = 0m;
        NewPocketGoal = null;
        NewPocketAnnualRatePercent = 0m;
        NewPocketTargetDate = null;
        await LoadAsync(ct);
    }

    private bool CanAddPocket() => !string.IsNullOrWhiteSpace(NewPocketName);
    partial void OnNewPocketNameChanged(string value) => AddPocketCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public void RecordDeposit() => RecordDepositRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void DepositToPocket(PocketRow? row)
    {
        if (row is null) return;
        DepositToPocketRequested?.Invoke(this, row);
    }

    /// <summary>Per-row edits persist immediately — keeps in-memory totals
    /// (like AutoSaveSum) in lock-step with the DB and avoids the "I
    /// zeroed everything but the summary still shows the seed values"
    /// trap of a manual Save button.</summary>
    private async void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PocketRow row) return;
        if (e.PropertyName is not (nameof(PocketRow.Name)
                                or nameof(PocketRow.CurrentBalance)
                                or nameof(PocketRow.AutoSavePercent)
                                or nameof(PocketRow.Goal)
                                or nameof(PocketRow.AnnualRatePercent)
                                or nameof(PocketRow.TargetDate))) return;

        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        var entity = await repo.FindAsync(row.Id);
        if (entity is null) return;
        entity.Name = row.Name;
        entity.CurrentBalance = row.CurrentBalance;
        entity.AutoSavePercent = row.AutoSavePercent;
        entity.Goal = row.Goal;
        entity.AnnualInterestRate = row.AnnualRatePercent / 100m;
        entity.TargetDate = row.TargetDate;
        await repo.UpdateAsync(entity);
        Recalculate();
    }

    [RelayCommand]
    public async Task ArchiveRowAsync(PocketRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;

        // Non-zero pockets need a "what about the money?" decision. Hand
        // off to the view to host the modal so this VM stays unit-testable
        // (no implicit Window.ShowDialog dependency).
        if (row.CurrentBalance != 0m)
        {
            ArchiveTransferRequested?.Invoke(this, row);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Archive \"{row.Name}\"? It won't appear in active totals but history stays on record.",
            "Archive pocket",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        await repo.ArchiveAsync(row.Id, ct);
        await LoadAsync(ct);
    }

    private void Recalculate()
    {
        TotalBalance = Pockets.Sum(p => p.CurrentBalance);
        ProjectedAnnualInterest = Pockets.Sum(p => p.CurrentBalance * (p.AnnualRatePercent / 100m));
        // Effective autosave = only pockets that will actually receive a share
        // (DepositService skips those at/over their goal). Stored % unchanged;
        // this is just how the warning should read for the next deposit.
        AutoSaveSum = Pockets
            .Where(p => !p.IsGoalReached)
            .Sum(p => p.AutoSavePercent);
        AutoSaveWarn = AutoSaveSum != 100m;
        AutoSaveWarning = AutoSaveSum switch
        {
            100m => "",
            < 100m => $"Active auto-save totals {AutoSaveSum:0.#}% — {(100m - AutoSaveSum):0.#}% of each deposit will be unallocated (goal-reached pockets don't receive).",
            _ => $"Active auto-save totals {AutoSaveSum:0.#}% — distributed totals will exceed each deposit.",
        };
    }
}

public sealed partial class PocketRow : ObservableObject
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public Guid Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private decimal _currentBalance;
    [ObservableProperty] private decimal _autoSavePercent;
    [ObservableProperty] private decimal? _goal;
    [ObservableProperty] private decimal _annualRatePercent;
    [ObservableProperty] private DateOnly? _targetDate;

    [ObservableProperty] private string _balanceDisplay;
    [ObservableProperty] private string _goalDisplay;
    [ObservableProperty] private double _goalProgress;
    [ObservableProperty] private string _progressDisplay;
    [ObservableProperty] private bool _hasGoal;

    /// <summary>"£X/mo needed" or "Hit goal" or "Past target" — rendered
    /// as a small caption beside the target date when the user sets one.</summary>
    [ObservableProperty] private string _targetProjection = "";
    [ObservableProperty] private bool _hasTarget;
    /// <summary>True when CurrentBalance ≥ Goal (and a goal is set).
    /// Drives the "autosave paused" UI caption + effective-autosave-sum calc.</summary>
    [ObservableProperty] private bool _isGoalReached;

    public PocketRow(Pocket source)
    {
        Id = source.Id;
        _name = source.Name;
        _currentBalance = source.CurrentBalance;
        _autoSavePercent = source.AutoSavePercent;
        _goal = source.Goal;
        _annualRatePercent = source.AnnualInterestRate * 100m;
        _targetDate = source.TargetDate;
        _balanceDisplay = source.CurrentBalance.ToString("C2", EnGb);
        (_goalDisplay, _goalProgress, _progressDisplay, _hasGoal) =
            ComputeProgress(source.CurrentBalance, source.Goal);
        _isGoalReached = source.Goal is > 0m && source.CurrentBalance >= source.Goal.Value;
        RefreshTargetProjection();
    }

    partial void OnCurrentBalanceChanged(decimal value)
    {
        BalanceDisplay = value.ToString("C2", EnGb);
        RefreshProgress();
        RefreshTargetProjection();
    }

    partial void OnGoalChanged(decimal? value)
    {
        RefreshProgress();
        RefreshTargetProjection();
    }

    partial void OnTargetDateChanged(DateOnly? value) => RefreshTargetProjection();

    private void RefreshProgress()
    {
        (GoalDisplay, GoalProgress, ProgressDisplay, HasGoal) =
            ComputeProgress(CurrentBalance, Goal);
        IsGoalReached = Goal is > 0m && CurrentBalance >= Goal.Value;
    }

    /// <summary>Updates the "£X/mo needed" caption based on goal, balance,
    /// and target date. Noop when any input is missing.</summary>
    private void RefreshTargetProjection()
    {
        HasTarget = TargetDate is not null && Goal is > 0m;
        if (!HasTarget)
        {
            TargetProjection = "";
            return;
        }
        var remainingToGoal = (Goal ?? 0m) - CurrentBalance;
        if (remainingToGoal <= 0m)
        {
            TargetProjection = $"Goal reached · target {TargetDate:dd MMM yyyy}";
            return;
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysToTarget = TargetDate!.Value.DayNumber - today.DayNumber;
        if (daysToTarget <= 0)
        {
            TargetProjection = $"Past target ({TargetDate:dd MMM yyyy}) · {remainingToGoal.ToString("C2", EnGb)} short";
            return;
        }
        // Ceiling months so a 45-day horizon reads as 2 months, not 1.5.
        var months = Math.Max(1, (int)Math.Ceiling(daysToTarget / 30.44));
        var perMonth = remainingToGoal / months;
        TargetProjection = $"~{perMonth.ToString("C2", EnGb)}/mo to hit by {TargetDate:dd MMM yyyy}";
    }

    private static (string goalDisplay, double progress, string progressDisplay, bool hasGoal)
        ComputeProgress(decimal balance, decimal? goal)
    {
        if (goal is null || goal == 0m)
            return ("", 0, "", false);
        var ratio = (double)(balance / goal.Value);
        if (ratio < 0) ratio = 0;
        return (
            goal.Value.ToString("C0", EnGb),
            Math.Min(ratio, 1.0) * 100,
            ratio.ToString("P0", EnGb),
            true);
    }
}
