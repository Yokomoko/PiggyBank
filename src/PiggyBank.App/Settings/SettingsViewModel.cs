using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Data;

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

    public IReadOnlyList<string> ThemeChoices { get; } = ["System", "Light", "Dark"];

    /// <summary>Raised when theme changes so the shell can re-apply the dictionary.</summary>
    public event EventHandler<string>? ThemeChanged;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        DataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PiggyBank", "app.db");
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
    }

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
