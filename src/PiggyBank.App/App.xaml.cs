using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PiggyBank.App.Hosting;
using PiggyBank.App.Profiles;
using PiggyBank.App.Settings;
using PiggyBank.App.Shell;
using PiggyBank.App.Views;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Profiles;

namespace PiggyBank.App;

public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack must run BEFORE any UI bootstraps so install / update
        // commands (--silent, --firstrun, --install, --uninstall) can
        // short-circuit normal startup. This is a no-op once the app is
        // launched normally post-install, so it's safe to leave on every run.
        Velopack.VelopackApp.Build().Run();

        // QuestPDF community licence acknowledgement — required before any
        // document is rendered. Set once, very first thing on startup.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        base.OnStartup(e);

        // Install global exception handlers BEFORE anything else can throw —
        // any failure in host startup or profile boot should go to the log,
        // not a silent crash. Architecture §10 mandates this.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Fire-and-forget the async boot so we don't hold up WPF's message
        // pump. All errors route through the handlers above.
        _ = BootAsync();
    }

    private async Task BootAsync()
    {
        try
        {
            _host = AppHost.Build();
            await _host.StartAsync();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();

            var sessions = _host.Services.GetRequiredService<IProfileSessionManager>();
            await sessions.EnsureInitialisedAsync();
            await ApplyStartupThemeAsync(sessions);

            var profileId = await ChooseOrCreateProfileAsync(sessions);
            if (profileId is null)
            {
                Shutdown();
                return;
            }

            sessions.OpenProfile(profileId.Value);

            using (var admin = sessions.OpenAdminScope())
            {
                var service = admin.Services.GetRequiredService<ProfileAdminService>();
                await service.TouchLastOpenedAsync(profileId.Value);
            }

            var profile = await GetProfileAsync(sessions, profileId.Value);
            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            shell.SetActiveProfile(profile);
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();

            // Now that the shell is up, shut down when IT closes (not any
            // prior dialog). ShutdownMode=OnExplicitShutdown is set in
            // App.xaml to prevent the app exiting when CreateProfileWindow
            // closes BEFORE MainWindow has a chance to show.
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ReportFatal("Startup failed", ex);
            Shutdown(1);
        }
    }

    private async Task<Guid?> ChooseOrCreateProfileAsync(IProfileSessionManager sessions)
    {
        while (true)
        {
            var anyProfile = await AnyProfileExistsAsync(sessions);
            if (!anyProfile)
            {
                var created = ShowCreateProfileDialog();
                if (created is null) return null;
                return created;
            }

            var picker = _host!.Services.GetRequiredService<ProfilePickerWindow>();
            var ok = picker.ShowDialog() == true;
            if (!ok) return null;

            if (picker.CreateRequested)
            {
                var created = ShowCreateProfileDialog();
                if (created is not null) return created;
                continue;
            }

            return picker.ChosenProfileId;
        }
    }

    private static async Task ApplyStartupThemeAsync(IProfileSessionManager sessions)
    {
        using var admin = sessions.OpenAdminScope();
        var db = admin.Services.GetRequiredService<PiggyBank.Data.AppDbContext>();
        var settings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.AppSettings);
        if (settings?.Theme is { } theme)
            SettingsWindow.ApplyTheme(theme);
    }

    private Guid? ShowCreateProfileDialog()
    {
        var window = _host!.Services.GetRequiredService<CreateProfileWindow>();
        return window.ShowDialog() == true ? window.CreatedProfileId : null;
    }

    private static async Task<bool> AnyProfileExistsAsync(IProfileSessionManager sessions)
    {
        using var admin = sessions.OpenAdminScope();
        var service = admin.Services.GetRequiredService<ProfileAdminService>();
        var list = await service.ListAsync();
        return list.Count > 0;
    }

    private static async Task<Profile?> GetProfileAsync(IProfileSessionManager sessions, Guid profileId)
    {
        using var admin = sessions.OpenAdminScope();
        var service = admin.Services.GetRequiredService<ProfileAdminService>();
        return await service.FindAsync(profileId);
    }

    // --- Exception handlers ---

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal("Unhandled dispatcher exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ReportFatal("Unhandled domain exception", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportFatal("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void ReportFatal(string headline, Exception ex)
    {
        _logger?.LogError(ex, headline);

        // Belt-and-braces: write to a crash file so the error is recoverable
        // even if the logger isn't wired yet (startup-path failures).
        try
        {
            // Logs live alongside the DB in %LocalAppData%\PiggyBankData\
            // — outside the Velopack install root so an upgrade never
            // wipes them. Crash artefacts surviving an upgrade is the
            // whole point of writing them to disk in the first place.
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PiggyBankData", "Logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, $"{headline}\n\n{ex}");
        }
        catch { /* last-resort, swallow */ }

        MessageBox.Show(
            $"{headline}\n\n{ex.Message}\n\nSee %LocalAppData%\\PiggyBankData\\Logs for details.",
            "PiggyBank — error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
