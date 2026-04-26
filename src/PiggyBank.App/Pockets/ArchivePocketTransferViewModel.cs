using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;

namespace PiggyBank.App.Pockets;

/// <summary>
/// Backs the "you can't archive a non-zero pocket" dialog. Two paths out:
///   1. Transfer the balance to another active pocket via
///      <see cref="DepositService.RecordToPocketAsync"/>, then archive.
///   2. Explicitly write off the balance (with a second-step confirm),
///      then archive.
/// </summary>
/// <remarks>
/// Mirrors the existing modal-VM pattern in this folder: takes the source
/// pocket via settable properties (set by the opening view before
/// <see cref="LoadAsync"/>), raises <see cref="Completed"/> when the host
/// window should close with <c>DialogResult = true</c>.
///
/// Edge case: if no other active pocket exists there's nothing to transfer
/// to. <see cref="HasTransferTargets"/> goes false and the UI hides the
/// transfer path, leaving only the explicit write-off + cancel.
/// </remarks>
public sealed partial class ArchivePocketTransferViewModel(IProfileSessionManager sessions) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public ObservableCollection<Pocket> TransferTargets { get; } = [];

    /// <summary>Source pocket id — set by the opening view before LoadAsync.</summary>
    public Guid SourcePocketId { get; set; }
    public string SourcePocketName { get; set; } = "";
    public decimal SourceBalance { get; set; }

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _balanceDisplay = "";
    [ObservableProperty] private Pocket? _selectedTarget;
    [ObservableProperty] private bool _hasTransferTargets;
    [ObservableProperty] private string _noTargetsMessage = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler? Completed;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        Headline = $"Archive \"{SourcePocketName}\"";
        BalanceDisplay = SourceBalance.ToString("C2", EnGb);

        var pockets = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
        TransferTargets.Clear();
        foreach (var p in await pockets.ListAsync(ct: ct))
        {
            // Don't offer the source pocket as its own target.
            if (p.Id == SourcePocketId) continue;
            TransferTargets.Add(p);
        }
        HasTransferTargets = TransferTargets.Count > 0;
        SelectedTarget = TransferTargets.FirstOrDefault();
        NoTargetsMessage = HasTransferTargets
            ? ""
            : "No other active pockets to transfer into. Either write off the balance or cancel and create another pocket first.";
        TransferAndArchiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTargetChanged(Pocket? value)
        => TransferAndArchiveCommand.NotifyCanExecuteChanged();

    partial void OnErrorMessageChanged(string value)
        => HasError = !string.IsNullOrWhiteSpace(value);

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public async Task TransferAndArchiveAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null || SelectedTarget is null) return;
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var deposits = _sessions.Current.Services.GetRequiredService<DepositService>();
            var pockets = _sessions.Current.Services.GetRequiredService<IPocketRepository>();

            var today = DateOnly.FromDateTime(DateTime.Today);
            var notes = $"Transfer from {SourcePocketName} before archiving";

            // Move the money into the target via the direct-to-pocket service
            // (skips autosave + goal-reached guard — exactly what we need here).
            await deposits.RecordToPocketAsync(SelectedTarget.Id, today, SourceBalance, notes, ct);

            // Zero the source explicitly. RecordToPocketAsync only touches the
            // target's balance; the source still shows the old amount until we
            // null it ourselves.
            var source = await pockets.FindAsync(SourcePocketId, ct);
            if (source is not null)
            {
                source.CurrentBalance = 0m;
                await pockets.UpdateAsync(source, ct);
            }

            await pockets.ArchiveAsync(SourcePocketId, ct);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }
        catch (ArgumentException ex)         { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task WriteOffAndArchiveAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;

        // Second-step confirm — explicit so an accidental click on a destructive
        // path still gives the user a way out before any DB writes.
        var confirm = System.Windows.MessageBox.Show(
            $"{BalanceDisplay} will be discarded — this can't be undone.\n\n" +
            $"Archive \"{SourcePocketName}\" and write off the balance?",
            "Write off balance?",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var pockets = _sessions.Current.Services.GetRequiredService<IPocketRepository>();
            var source = await pockets.FindAsync(SourcePocketId, ct);
            if (source is not null)
            {
                source.CurrentBalance = 0m;
                await pockets.UpdateAsync(source, ct);
            }
            await pockets.ArchiveAsync(SourcePocketId, ct);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }
        catch (ArgumentException ex)         { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private bool CanTransfer() => HasTransferTargets && SelectedTarget is not null && !IsBusy;
}
