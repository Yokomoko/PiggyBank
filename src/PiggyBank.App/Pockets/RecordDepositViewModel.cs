using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;

namespace PiggyBank.App.Pockets;

public sealed partial class RecordDepositViewModel(
    IProfileSessionManager sessions,
    TimeProvider clock) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly TimeProvider _clock = clock;

    public ObservableCollection<DepositPreviewRow> Preview { get; } = [];

    [ObservableProperty] private DateOnly _depositedOn = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _unallocatedDisplay = "";
    [ObservableProperty] private bool _hasUnallocated;

    /// <summary>When non-null, the deposit lands 100% in this pocket and
    /// skips the autosave distribution + goal-reached guard. Set by the
    /// view before opening the window.</summary>
    [ObservableProperty] private Guid? _targetPocketId;
    [ObservableProperty] private string _targetPocketName = "";
    [ObservableProperty] private string _headline = "Record deposit";
    [ObservableProperty] private string _caption = "Auto-distributed into your pockets using each pocket's current AutoSave %.";

    public event EventHandler? Completed;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        DepositedOn = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        // Direct-to-pocket mode is locked in before Load fires (via the
        // opening view), so reflect it in the headline/caption upfront.
        if (TargetPocketId is not null)
        {
            Headline = $"Deposit to {TargetPocketName}";
            Caption = "Direct deposit — goes 100% to this pocket, skips goal-reached guard.";
        }
        await RefreshPreviewAsync(ct);
    }

    partial void OnAmountChanged(decimal value)
    {
        _ = RefreshPreviewAsync();
        RecordCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshPreviewAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;

        Preview.Clear();

        // Direct-to-pocket: preview is a single row, whole amount.
        if (TargetPocketId is not null)
        {
            Preview.Add(new DepositPreviewRow(TargetPocketName, 100m, Amount));
            HasUnallocated = false;
            UnallocatedDisplay = "";
            return;
        }

        var pockets = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        var list = await pockets.ListAsync(ct: ct);

        var distributed = 0m;
        foreach (var p in list)
        {
            if (p.AutoSavePercent <= 0m) continue;
            // Mirror DepositService: skip pockets that have reached their goal.
            if (p.Goal is > 0m && p.CurrentBalance >= p.Goal) continue;
            var share = Math.Round(Amount * (p.AutoSavePercent / 100m), 2, MidpointRounding.AwayFromZero);
            Preview.Add(new DepositPreviewRow(p.Name, p.AutoSavePercent, share));
            distributed += share;
        }

        var unallocated = Amount - distributed;
        HasUnallocated = unallocated != 0m;
        UnallocatedDisplay = unallocated.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
    }

    [RelayCommand(CanExecute = nameof(CanRecord))]
    public async Task RecordAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var svc = _sessions.Current.Services.GetRequiredService<DepositService>();
        var trimmedNotes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        if (TargetPocketId is Guid targetId)
            await svc.RecordToPocketAsync(targetId, DepositedOn, Amount, trimmedNotes, ct);
        else
            await svc.RecordAsync(DepositedOn, Amount, trimmedNotes, ct);

        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRecord() => Amount > 0m;
}

public sealed record DepositPreviewRow(string PocketName, decimal Percent, decimal Share);
