using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Profiles;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Joint;

/// <summary>
/// View model for the cross-profile Joint view. Lists every shared
/// <see cref="JointAccount"/> with its contributions and outgoings, and
/// computes the household-level surplus / shortfall headline per account.
/// </summary>
/// <remarks>
/// Joint data is shared across both profiles, so we deliberately read
/// it through <see cref="IJointRepository"/> (which doesn't apply a
/// tenant filter), not through any ProfileOwned table. Profile display
/// names for the contributions table are resolved via the admin scope
/// and <see cref="ProfileAdminService"/>: the only legitimate cross-
/// profile lookup in the app.
/// </remarks>
public sealed partial class JointViewModel(
    IProfileSessionManager sessions,
    TimeProvider clock) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly TimeProvider _clock = clock;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public ObservableCollection<JointAccountRow> Accounts { get; } = [];

    /// <summary>All non-archived profiles, used to populate the "+ contribution"
    /// row's profile picker. Loaded once via the admin scope on
    /// <see cref="LoadAsync"/>.</summary>
    public ObservableCollection<ProfileOption> Profiles { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasAccounts;

    // --- Add-account form (renders inside the empty state, AND above the list) ---
    [ObservableProperty] private string _newAccountName = "";
    [ObservableProperty] private string _newAccountBank = "";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            // Profile lookup runs through the admin scope — the only
            // sanctioned cross-tenant read path in the app. Build a name map
            // so the contribution rows can render the contributor's display
            // name (e.g. "Profile A: £500") without leaking GUIDs into the UI.
            var profileMap = new Dictionary<Guid, string>();
            using (var admin = _sessions.OpenAdminScope())
            {
                var adminSvc = admin.Services.GetRequiredService<ProfileAdminService>();
                var profiles = await adminSvc.ListAsync(ct);
                Profiles.Clear();
                foreach (var p in profiles)
                {
                    profileMap[p.Id] = p.DisplayName;
                    Profiles.Add(new ProfileOption(p.Id, p.DisplayName, p.ColourHex));
                }
            }

            var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
            var accounts = await repo.ListAccountsAsync(ct: ct);

            foreach (var row in Accounts)
            {
                row.PropertyChanged -= OnAccountChanged;
                foreach (var c in row.Contributions) c.PropertyChanged -= OnContributionChanged;
                foreach (var o in row.Outgoings) o.PropertyChanged -= OnOutgoingChanged;
            }
            Accounts.Clear();

            foreach (var account in accounts)
            {
                var contributions = await repo.ListContributionsAsync(account.Id, ct);
                var outgoings = await repo.ListOutgoingsAsync(account.Id, ct);

                var row = new JointAccountRow(account);
                foreach (var c in contributions)
                {
                    var displayName = profileMap.TryGetValue(c.ProfileId, out var n) ? n : "(unknown)";
                    var crow = new JointContributionRow(c, displayName);
                    crow.PropertyChanged += OnContributionChanged;
                    row.Contributions.Add(crow);
                }
                foreach (var o in outgoings)
                {
                    var orow = new JointOutgoingRow(o);
                    orow.PropertyChanged += OnOutgoingChanged;
                    row.Outgoings.Add(orow);
                }
                row.Recalculate();
                row.PropertyChanged += OnAccountChanged;
                Accounts.Add(row);
            }

            HasAccounts = Accounts.Count > 0;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAddAccount))]
    public async Task AddAccountAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.AddAccountAsync(new JointAccount
        {
            Name = NewAccountName.Trim(),
            BankName = string.IsNullOrWhiteSpace(NewAccountBank) ? null : NewAccountBank.Trim(),
            SortOrder = Accounts.Count,
        }, ct);
        NewAccountName = "";
        NewAccountBank = "";
        await LoadAsync(ct);
    }

    private bool CanAddAccount() => !string.IsNullOrWhiteSpace(NewAccountName);
    partial void OnNewAccountNameChanged(string value) => AddAccountCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task ArchiveAccountAsync(JointAccountRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Archive \"{row.Name}\"? Its contributions and outgoings stay on file but stop counting toward totals.",
            "Archive joint account",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.ArchiveAccountAsync(row.Id, ct);
        await LoadAsync(ct);
    }

    [RelayCommand]
    public async Task AddContributionAsync(JointAccountRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        if (row.NewContributionProfile is null) return;
        if (row.NewContributionAmount <= 0m) return;

        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = row.Id,
            ProfileId = row.NewContributionProfile.Id,
            MonthlyAmount = row.NewContributionAmount,
        }, ct);
        row.NewContributionProfile = null;
        row.NewContributionAmount = 0m;
        await LoadAsync(ct);
    }

    [RelayCommand]
    public async Task DeleteContributionAsync(JointContributionRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.DeleteContributionAsync(row.Id, ct);
        await LoadAsync(ct);
    }

    [RelayCommand]
    public async Task AddOutgoingAsync(JointAccountRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        if (string.IsNullOrWhiteSpace(row.NewOutgoingName)) return;

        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = row.Id,
            Name = row.NewOutgoingName.Trim(),
            // Outgoings stored negative to match the MonthlyOutgoing convention.
            Amount = -Math.Abs(row.NewOutgoingAmount),
            SortOrder = row.Outgoings.Count,
        }, ct);
        row.NewOutgoingName = "";
        row.NewOutgoingAmount = 0m;
        await LoadAsync(ct);
    }

    [RelayCommand]
    public async Task DeleteOutgoingAsync(JointOutgoingRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        await repo.DeleteOutgoingAsync(row.Id, ct);
        await LoadAsync(ct);
    }

    /// <summary>Persist edits made in-place on an account (name, bank).</summary>
    private async void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not JointAccountRow row) return;
        if (e.PropertyName is not (nameof(JointAccountRow.Name) or nameof(JointAccountRow.BankName))) return;

        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        var entity = await repo.FindAccountAsync(row.Id);
        if (entity is null) return;
        entity.Name = row.Name;
        entity.BankName = string.IsNullOrWhiteSpace(row.BankName) ? null : row.BankName;
        await repo.UpdateAccountAsync(entity);
    }

    /// <summary>Persist inline contribution edits (amount). Assigning a new
    /// profile to an existing row isn't supported through the inline edit
    /// path — the user deletes and re-adds, which keeps the audit clean.</summary>
    private async void OnContributionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not JointContributionRow row) return;
        if (e.PropertyName is not nameof(JointContributionRow.MonthlyAmount)) return;

        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        var contributions = await repo.ListContributionsAsync(row.JointAccountId);
        var entity = contributions.FirstOrDefault(c => c.Id == row.Id);
        if (entity is null) return;
        entity.MonthlyAmount = row.MonthlyAmount;
        await repo.UpdateContributionAsync(entity);
        // Recompute the headline strip on the parent account so the user
        // sees the new surplus / shortfall the moment they tab out.
        var parent = Accounts.FirstOrDefault(a => a.Id == row.JointAccountId);
        parent?.Recalculate();
    }

    private async void OnOutgoingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not JointOutgoingRow row) return;
        if (e.PropertyName is not (nameof(JointOutgoingRow.Name) or nameof(JointOutgoingRow.AmountInput)))
            return;

        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<IJointRepository>();
        var outgoings = await repo.ListOutgoingsAsync(row.JointAccountId);
        var entity = outgoings.FirstOrDefault(o => o.Id == row.Id);
        if (entity is null) return;
        entity.Name = row.Name;
        entity.Amount = -Math.Abs(row.AmountInput);
        await repo.UpdateOutgoingAsync(entity);
        var parent = Accounts.FirstOrDefault(a => a.Id == row.JointAccountId);
        parent?.Recalculate();
    }
}

/// <summary>One row per joint account. Owns its contribution + outgoing
/// child collections plus the per-account add-row form state.</summary>
public sealed partial class JointAccountRow : ObservableObject
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public Guid Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _bankName;

    public ObservableCollection<JointContributionRow> Contributions { get; } = [];
    public ObservableCollection<JointOutgoingRow> Outgoings { get; } = [];

    /// <summary>Aggregate monthly contributions across all profiles.</summary>
    [ObservableProperty] private decimal _contributionsTotal;
    /// <summary>Sum of outgoing magnitudes (positive number).</summary>
    [ObservableProperty] private decimal _outgoingsTotal;
    /// <summary>Contributions minus |Outgoings|. Negative = shortfall.</summary>
    [ObservableProperty] private decimal _surplus;
    [ObservableProperty] private bool _isShortfall;
    /// <summary>Magnitude of the shortfall when <see cref="IsShortfall"/>.</summary>
    [ObservableProperty] private decimal _topUpNeeded;

    [ObservableProperty] private string _headlineText = "";
    [ObservableProperty] private string _surplusText = "";

    /// <summary>True when there are no contribution rows yet — drives the
    /// "Add a contribution to get started" caption inside the table.</summary>
    [ObservableProperty] private bool _hasNoContributions;
    /// <summary>Mirror of the above for the outgoings column.</summary>
    [ObservableProperty] private bool _hasNoOutgoings;

    // --- Per-row "add contribution" form ---
    [ObservableProperty] private ProfileOption? _newContributionProfile;
    [ObservableProperty] private decimal _newContributionAmount;

    // --- Per-row "add outgoing" form ---
    [ObservableProperty] private string _newOutgoingName = "";
    [ObservableProperty] private decimal _newOutgoingAmount;

    public JointAccountRow(JointAccount source)
    {
        Id = source.Id;
        _name = source.Name;
        _bankName = source.BankName ?? "";
    }

    public void Recalculate()
    {
        ContributionsTotal = Contributions.Sum(c => c.MonthlyAmount);
        OutgoingsTotal = Outgoings.Sum(o => Math.Abs(o.Amount));
        Surplus = ContributionsTotal - OutgoingsTotal;
        IsShortfall = Surplus < 0m;
        TopUpNeeded = IsShortfall ? -Surplus : 0m;

        HasNoContributions = Contributions.Count == 0;
        HasNoOutgoings = Outgoings.Count == 0;

        var c = ContributionsTotal.ToString("C2", EnGb);
        var o = OutgoingsTotal.ToString("C2", EnGb);
        HeadlineText = $"In {c}  ·  Out {o}";
        SurplusText = IsShortfall
            ? $"Top up needed: {TopUpNeeded.ToString("C2", EnGb)}"
            : $"Surplus: {Surplus.ToString("C2", EnGb)}";
    }
}

/// <summary>One row in the contributions table.</summary>
public sealed partial class JointContributionRow : ObservableObject
{
    public Guid Id { get; }
    public Guid JointAccountId { get; }
    public Guid ProfileId { get; }
    public string ProfileDisplayName { get; }

    [ObservableProperty] private decimal _monthlyAmount;

    public JointContributionRow(JointContribution source, string profileDisplayName)
    {
        Id = source.Id;
        JointAccountId = source.JointAccountId;
        ProfileId = source.ProfileId;
        ProfileDisplayName = profileDisplayName;
        _monthlyAmount = source.MonthlyAmount;
    }
}

/// <summary>One row in the outgoings table. <see cref="AmountInput"/> is
/// the positive magnitude shown in the box; the VM persists it negative
/// to match the outgoing-storage convention.</summary>
public sealed partial class JointOutgoingRow : ObservableObject
{
    public Guid Id { get; }
    public Guid JointAccountId { get; }
    public decimal Amount { get; private set; }

    [ObservableProperty] private string _name;
    /// <summary>Editable positive magnitude shown in the row's NumberBox.</summary>
    [ObservableProperty] private decimal _amountInput;

    public JointOutgoingRow(JointOutgoing source)
    {
        Id = source.Id;
        JointAccountId = source.JointAccountId;
        _name = source.Name;
        Amount = source.Amount;
        _amountInput = Math.Abs(source.Amount);
    }
}

/// <summary>Lightweight projection of a profile for the contribution-row
/// picker. Stores the colour so the picker can show a chip alongside the
/// name without touching <see cref="Profile"/> directly.</summary>
public sealed record ProfileOption(Guid Id, string DisplayName, string ColourHex)
{
    public override string ToString() => DisplayName;
}
