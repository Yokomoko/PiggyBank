using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PiggyBank.App.Analytics;
using PiggyBank.App.Dashboard;
using PiggyBank.App.Debts;
using PiggyBank.App.Joint;
using PiggyBank.App.Pockets;
using PiggyBank.App.Profiles;
using PiggyBank.App.Settings;
using PiggyBank.App.SideIncome;
using PiggyBank.App.Theming;
using PiggyBank.App.Views;
using PiggyBank.Core.Budgeting;
using PiggyBank.Data;

namespace PiggyBank.App.Hosting;

/// <summary>
/// Boots the <see cref="IHost"/> that backs the whole WPF app. The host
/// owns root DI; <see cref="ProfileSessionManager"/> owns per-profile
/// scopes created off that root.
/// </summary>
public static class AppHost
{
    public static IHost Build()
    {
        // E2E tests and portability: respect PIGGYBANK_DATA_ROOT if set, so
        // an isolated DB can be used per test run. Production launches put
        // the data in a Data/ subfolder of %LocalAppData%\PiggyBank so it
        // doesn't collide with Velopack's install layout (current/, packages/).
        // Without the subfolder, Velopack sees the bare app.db at the install
        // root and reports "PiggyBank is already installed" on first run.
        var envRoot = Environment.GetEnvironmentVariable("PIGGYBANK_DATA_ROOT");
        var dataDir = !string.IsNullOrWhiteSpace(envRoot)
            ? envRoot
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PiggyBank",
                "Data");
        Directory.CreateDirectory(dataDir);

        // One-shot migration: if a legacy app.db lives at the parent dir
        // (pre-Data/ subfolder layout), move it before opening the new path.
        // Saves users hand-copying their database after the v0.1.0 -> v0.1.1 update.
        var legacyDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PiggyBank", "app.db");
        var newDbPath = Path.Combine(dataDir, "app.db");
        if (string.IsNullOrWhiteSpace(envRoot)
            && File.Exists(legacyDbPath)
            && !File.Exists(newDbPath))
        {
            File.Move(legacyDbPath, newDbPath);
        }

        var connectionString = $"Data Source={newDbPath};Foreign Keys=True;";

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddPiggyBankData(connectionString);

        // Core (no DI needed today, but register pure services for VM consumption)
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<BudgetCalculator>();

        // App-level session manager & windows
        builder.Services.AddSingleton<ProfileSessionManager>();
        builder.Services.AddSingleton<IProfileSessionManager>(
            sp => sp.GetRequiredService<ProfileSessionManager>());
        builder.Services.AddSingleton<IThemeService, WpfUiThemeService>();
        builder.Services.AddTransient<ProfilePickerWindow>();
        builder.Services.AddTransient<ProfilePickerViewModel>();
        builder.Services.AddTransient<CreateProfileWindow>();
        builder.Services.AddTransient<CreateProfileViewModel>();
        builder.Services.AddTransient<CurrentMonthView>();
        builder.Services.AddTransient<CurrentMonthViewModel>();
        builder.Services.AddTransient<AnalyticsView>();
        builder.Services.AddTransient<AnalyticsViewModel>();
        builder.Services.AddTransient<PastMonthWindow>();
        builder.Services.AddTransient<PastMonthViewModel>();
        builder.Services.AddTransient<DebtsView>();
        builder.Services.AddTransient<DebtsViewModel>();
        builder.Services.AddTransient<PocketsView>();
        builder.Services.AddTransient<PocketsViewModel>();
        builder.Services.AddTransient<RecordDepositWindow>();
        builder.Services.AddTransient<RecordDepositViewModel>();
        builder.Services.AddTransient<ArchivePocketTransferWindow>();
        builder.Services.AddTransient<ArchivePocketTransferViewModel>();
        builder.Services.AddTransient<SideIncomeView>();
        builder.Services.AddTransient<SideIncomeViewModel>();
        builder.Services.AddTransient<JointView>();
        builder.Services.AddTransient<JointViewModel>();
        builder.Services.AddTransient<AllocateSideIncomeWindow>();
        builder.Services.AddTransient<AllocateSideIncomeViewModel>();
        builder.Services.AddTransient<SettingsWindow>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<MainWindow>();

        return builder.Build();
    }
}
