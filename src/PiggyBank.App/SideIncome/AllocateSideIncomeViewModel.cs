using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;

namespace PiggyBank.App.SideIncome;

public sealed partial class AllocateSideIncomeViewModel(IProfileSessionManager sessions) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public ObservableCollection<Pocket> Pockets { get; } = [];

    /// <summary>Entry being allocated from — set by the opening view.</summary>
    public Guid EntryId { get; set; }
    public DateOnly EntryPaidOn { get; set; }
    public string EntryDescription { get; set; } = "";
    public decimal EntryRemaining { get; set; }

    /// <summary>Month-mode: when set, <see cref="EntryId"/> is ignored and
    /// every entry in this list gets its remaining flowed to the chosen
    /// target (one allocation per entry — no cross-subsidy).</summary>
    public IReadOnlyList<SideIncomeEntryRow>? MonthEntries { get; set; }
    public string MonthLabel { get; set; } = "";

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _remainingDisplay = "";
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private bool _amountEditable = true;
    [ObservableProperty] private bool _targetPocket = true;
    [ObservableProperty] private bool _targetMainLedger;
    [ObservableProperty] private Pocket? _selectedPocket;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isMonthMode;

    public event EventHandler? Completed;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var pockets = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        Pockets.Clear();
        foreach (var p in await pockets.ListAsync(ct: ct)) Pockets.Add(p);
        SelectedPocket = Pockets.FirstOrDefault();

        if (MonthEntries is { Count: > 0 })
        {
            IsMonthMode = true;
            var monthRemaining = MonthEntries.Sum(e => e.Remaining);
            Headline = $"Allocate month: {MonthLabel}";
            RemainingDisplay = $"Remaining across {MonthEntries.Count} entr{(MonthEntries.Count == 1 ? "y" : "ies")}: {monthRemaining.ToString("C2", EnGb)}";
            Amount = monthRemaining;
            AmountEditable = false;  // month mode is all-or-nothing per entry
        }
        else
        {
            IsMonthMode = false;
            Headline = $"Allocate from: {(string.IsNullOrWhiteSpace(EntryDescription) ? EntryPaidOn.ToString("dd MMM yyyy", EnGb) : EntryDescription)}";
            RemainingDisplay = $"Remaining: {EntryRemaining.ToString("C2", EnGb)}";
            Amount = EntryRemaining;
            AmountEditable = true;
        }
    }

    partial void OnTargetPocketChanged(bool value)
    {
        if (value) TargetMainLedger = false;
    }

    partial void OnTargetMainLedgerChanged(bool value)
    {
        if (value) TargetPocket = false;
    }

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrWhiteSpace(value);

    [RelayCommand(CanExecute = nameof(CanAllocate))]
    public async Task AllocateAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";
        if (_sessions.Current is null) return;
        var svc = _sessions.Current.Services.GetRequiredService<SideIncomeService>();
        var trimmedNotes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        try
        {
            if (IsMonthMode)
            {
                if (TargetPocket && SelectedPocket is null)
                {
                    ErrorMessage = "Pick a pocket first.";
                    return;
                }
                // Loop each entry in the month with its own remaining — keeps
                // the service's per-entry guard rails in effect and surfaces
                // closed-month / no-month errors on a per-entry basis.
                foreach (var entry in MonthEntries!.Where(e => e.Remaining > 0m))
                {
                    if (TargetPocket)
                        await svc.AllocateToPocketAsync(entry.Id, SelectedPocket!.Id, entry.Remaining, trimmedNotes, ct);
                    else
                        await svc.AllocateToMainLedgerAsync(entry.Id, entry.Remaining, trimmedNotes, ct);
                }
            }
            else
            {
                if (TargetPocket)
                {
                    if (SelectedPocket is null) { ErrorMessage = "Pick a pocket first."; return; }
                    await svc.AllocateToPocketAsync(EntryId, SelectedPocket.Id, Amount, trimmedNotes, ct);
                }
                else
                {
                    await svc.AllocateToMainLedgerAsync(EntryId, Amount, trimmedNotes, ct);
                }
            }
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }
        catch (ArgumentException ex)         { ErrorMessage = ex.Message; }
    }

    private bool CanAllocate() =>
        IsMonthMode
            ? Amount > 0m  // always the full month remaining, already computed
            : Amount > 0m && Amount <= EntryRemaining;

    partial void OnAmountChanged(decimal value) => AllocateCommand.NotifyCanExecuteChanged();
}
