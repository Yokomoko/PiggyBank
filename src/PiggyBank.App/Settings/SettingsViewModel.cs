using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Repositories;

namespace PiggyBank.App.Settings;

public sealed partial class SettingsViewModel(IProfileSessionManager sessions) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private AppSettings? _systemSettings;
    private ProfileSettings? _profileSettings;

    [ObservableProperty] private string _theme = "System";
    [ObservableProperty] private string _dataPath = "";
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private decimal _dailyFoodBudget = 45m;
    [ObservableProperty] private decimal _bufferPerDay = 10m;
    [ObservableProperty] private int _primaryPaydayDayOfMonth = 25;
    [ObservableProperty] private bool _adjustPaydayForWeekendsAndBankHolidays = true;
    [ObservableProperty] private string _statusMessage = "";

    // --- Categories panel ---
    public ObservableCollection<CategoryRow> Categories { get; } = [];
    public IReadOnlyList<CategoryKindOption> CategoryKindChoices { get; } = CategoryKindOption.All;

    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private CategoryKindOption _newCategoryKind = CategoryKindOption.All[0];

    public IReadOnlyList<string> ThemeChoices { get; } = ["System", "Light", "Dark"];

    /// <summary>Raised when theme changes so the shell can re-apply the dictionary.</summary>
    public event EventHandler<string>? ThemeChanged;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        DataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PiggyBankData", "app.db");
        AppVersion = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "dev";

        using (var admin = _sessions.OpenAdminScope())
        {
            var db = admin.Services.GetRequiredService<AppDbContext>();
            _systemSettings = await db.AppSettings.FirstOrDefaultAsync(ct);
            if (_systemSettings is not null) Theme = _systemSettings.Theme;
        }

        if (_sessions.Current is not null)
        {
            var db = _sessions.Current.Services.GetRequiredService<AppDbContext>();
            _profileSettings = await db.ProfileSettings.FirstOrDefaultAsync(ct);
            if (_profileSettings is not null)
            {
                DailyFoodBudget = _profileSettings.DailyFoodBudget;
                BufferPerDay = _profileSettings.BufferPerDay;
                PrimaryPaydayDayOfMonth = _profileSettings.PrimaryPaydayDayOfMonth;
                AdjustPaydayForWeekendsAndBankHolidays = _profileSettings.AdjustPaydayForWeekendsAndBankHolidays;
            }
        }

        await ReloadCategoriesAsync(ct);
    }

    /// <summary>Refreshes the Categories list from the active profile scope.
    /// Pulled out so the add/archive commands can call it without re-running
    /// the full settings load.</summary>
    private async Task ReloadCategoriesAsync(CancellationToken ct = default)
    {
        Categories.Clear();
        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<ICategoryRepository>();
        foreach (var c in await repo.ListAsync(ct: ct))
            Categories.Add(new CategoryRow(c.Id, c.Name, c.Kind, KindLabel(c.Kind)));
    }

    [RelayCommand]
    public async Task AddCategoryAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var name = NewCategoryName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var repo = _sessions.Current.Services.GetRequiredService<ICategoryRepository>();
        await repo.AddAsync(name, NewCategoryKind.Kind, ct: ct);

        NewCategoryName = "";
        NewCategoryKind = CategoryKindChoices[0];
        await ReloadCategoriesAsync(ct);
        StatusMessage = $"Added \"{name}\".";
    }

    [RelayCommand]
    public async Task ArchiveCategoryAsync(CategoryRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Archive \"{row.Name}\"?\n\n" +
            "Existing transactions tagged with this category keep the tag — only " +
            "the picker hides it. You can't reverse this from the UI yet.",
            "Archive category",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<ICategoryRepository>();
        await repo.ArchiveAsync(row.Id, ct);
        await ReloadCategoriesAsync(ct);
        StatusMessage = $"Archived \"{row.Name}\".";
    }

    private static string KindLabel(CategoryKind kind) => kind switch
    {
        CategoryKind.Spend       => "Spend",
        CategoryKind.Income      => "Income",
        CategoryKind.Savings     => "Savings",
        CategoryKind.Overpayment => "Overpayment",
        _                        => kind.ToString(),
    };

    [RelayCommand]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        using (var admin = _sessions.OpenAdminScope())
        {
            var db = admin.Services.GetRequiredService<AppDbContext>();
            var settings = _systemSettings ?? new AppSettings { Id = 1 };
            settings.Theme = Theme;
            if (_systemSettings is null) db.AppSettings.Add(settings);
            await db.SaveChangesAsync(ct);
            _systemSettings = settings;
        }

        if (_sessions.Current is not null && _profileSettings is not null)
        {
            var db = _sessions.Current.Services.GetRequiredService<AppDbContext>();
            _profileSettings.DailyFoodBudget = DailyFoodBudget;
            _profileSettings.BufferPerDay = BufferPerDay;
            _profileSettings.PrimaryPaydayDayOfMonth = PrimaryPaydayDayOfMonth;
            _profileSettings.AdjustPaydayForWeekendsAndBankHolidays = AdjustPaydayForWeekendsAndBankHolidays;
            db.ProfileSettings.Update(_profileSettings);
            await db.SaveChangesAsync(ct);
        }

        ThemeChanged?.Invoke(this, Theme);
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}.";
    }
}

public sealed record CategoryRow(Guid Id, string Name, CategoryKind Kind, string KindLabel);

/// <summary>ComboBox-friendly pairing of <see cref="CategoryKind"/> with a
/// human label so the picker shows "Spend" rather than "Spend = 1".</summary>
public sealed record CategoryKindOption(CategoryKind Kind, string Label)
{
    public static IReadOnlyList<CategoryKindOption> All { get; } =
    [
        new(CategoryKind.Spend,       "Spend"),
        new(CategoryKind.Income,      "Income"),
        new(CategoryKind.Savings,     "Savings"),
        new(CategoryKind.Overpayment, "Overpayment"),
    ];

    public override string ToString() => Label;
}
