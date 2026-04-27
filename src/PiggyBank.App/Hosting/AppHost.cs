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
using PiggyBank.App.Shell;
using PiggyBank.App.SideIncome;
using PiggyBank.App.Theming;
using PiggyBank.App.Updates;
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
        // E2E tests and portability: PIGGYBANK_DATA_ROOT overrides the
        // production path so an isolated DB can be used per test run.
        //
        // Production data lives in %LocalAppData%\PiggyBankData\ — a SIBLING
        // of the Velopack install root (%LocalAppData%\PiggyBank\), not a
        // subfolder. Velopack treats its install root as its own and wipes
        // everything inside it during in-place upgrades, so any data
        // subfolder we leave there gets nuked on the next setup.exe. Living
        // outside that tree is the only safe place for user data.
        //
        // Layout history (each release migrates forward on first launch):
        //   v0.1.0:        %LocalAppData%\PiggyBank\app.db (collided with Velopack)
        //   v0.1.1-v0.2.1: %LocalAppData%\PiggyBank\Data\app.db (still inside Velopack root — wiped on upgrade)
        //   v0.2.2+:       %LocalAppData%\PiggyBankData\app.db (safe).
        var envRoot = Environment.GetEnvironmentVariable("PIGGYBANK_DATA_ROOT");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = !string.IsNullOrWhiteSpace(envRoot)
            ? envRoot
            : Path.Combine(localAppData, "PiggyBankData");
        Directory.CreateDirectory(dataDir);
        var newDbPath = Path.Combine(dataDir, "app.db");

        // One-shot migrations from older layouts. Only attempt when the new
        // location is empty — once the user is on the safe path we never
        // fall back to the wipe-prone ones.
        if (string.IsNullOrWhiteSpace(envRoot) && !File.Exists(newDbPath))
        {
            var legacyDataFolder = Path.Combine(localAppData, "PiggyBank", "Data", "app.db");
            var legacyRoot = Path.Combine(localAppData, "PiggyBank", "app.db");
            var source = File.Exists(legacyDataFolder) ? legacyDataFolder
                       : File.Exists(legacyRoot) ? legacyRoot
                       : null;
            if (source is not null)
                MoveDbWithSidecars(source, newDbPath);
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
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<ShellViewModel>();
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
        builder.Services.AddTransient<CompareMonthsWindow>();
        builder.Services.AddTransient<CompareMonthsViewModel>();
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

    /// <summary>Moves an SQLite database file plus its WAL/SHM sidecars
    /// from the legacy location to the new location. Without bringing the
    /// sidecars along SQLite would treat the move as a partial recovery
    /// scenario and the user could lose recent committed transactions
    /// that hadn't yet checkpointed back into the main file.</summary>
    private static void MoveDbWithSidecars(string from, string to)
    {
        File.Move(from, to);
        foreach (var sidecar in new[] { "-shm", "-wal" })
        {
            var src = from + sidecar;
            if (File.Exists(src)) File.Move(src, to + sidecar);
        }
    }
}
