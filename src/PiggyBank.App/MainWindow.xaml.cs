using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Analytics;
using PiggyBank.App.Dashboard;
using PiggyBank.App.Debts;
using PiggyBank.App.Joint;
using PiggyBank.App.Pockets;
using PiggyBank.App.Settings;
using PiggyBank.App.Shell;
using PiggyBank.App.Updates;
using Wpf.Ui.Controls;

namespace PiggyBank.App;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private readonly UpdateService _updates;

    public MainWindow(
        ShellViewModel shell,
        CurrentMonthView currentMonth,
        IServiceProvider services,
        UpdateService updates)
    {
        InitializeComponent();
        TryApplyWindowIcon();
        DataContext = shell;
        RootContent.Content = currentMonth;
        _services = services;
        _updates = updates;
        _updates.UpdateReady += OnUpdateReady;
        // Trigger the background check after the shell has rendered so
        // first paint is never delayed by HTTP. UpdateService is itself
        // idempotent and fail-safe; this just kicks the wheel.
        Loaded += (_, _) => _updates.StartBackgroundCheck();
    }

    private void OnUpdateReady(object? sender, UpdateReadyEventArgs e)
    {
        // The event fires on a background thread; marshal back before
        // touching any WPF surface.
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var version = e.Info.TargetFullRelease.Version;
                var result = System.Windows.MessageBox.Show(
                    $"PiggyBank {version} is ready to install.\n\n" +
                    "Restart now to apply, or pick Later to install on your next launch.",
                    "Update ready",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Information);
                if (result != System.Windows.MessageBoxResult.OK) return;

                try
                {
                    e.Manager.ApplyUpdatesAndRestart(e.Info);
                }
                catch
                {
                    // ApplyUpdatesAndRestart launches the Update.exe and
                    // exits the process; if it throws (locked file,
                    // permissions, etc.) the user keeps the running app
                    // and the downloaded package will install on next
                    // launch. No further action needed here.
                }
            }
            catch
            {
                // Last-resort: never let a broken update prompt take down
                // the shell. The update remains downloaded and will apply
                // on next launch via Velopack's standard flow.
            }
        });
    }

    private void OnNavCurrentMonthClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<CurrentMonthView>();
    }

    private void OnNavAnalyticsClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<AnalyticsView>();
    }

    private void OnNavDebtsClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<DebtsView>();
    }

    private void OnNavPocketsClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<PocketsView>();
    }

    private void OnNavJointClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<JointView>();
    }

    private void OnNavSideIncomeClicked(object sender, RoutedEventArgs e)
    {
        RootContent.Content = _services.GetRequiredService<PiggyBank.App.SideIncome.SideIncomeView>();
    }

    /// <summary>Loads the embedded PiggyBank.ico resource and sets it as
    /// the window's title-bar icon. Wrapped in try/catch because a XAML
    /// Icon attribute set to an unresolved pack URI is unrecoverable
    /// (the whole BAML parse fails and the app crashes on startup).
    /// Doing it programmatically lets us silently fall back to the
    /// embedded EXE icon — which still drives the taskbar — if anything
    /// goes wrong with the resource lookup.</summary>
    private void TryApplyWindowIcon()
    {
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/PiggyBank.ico", UriKind.Absolute));
        }
        catch
        {
            // Fall through — Window.Icon stays null, taskbar uses the
            // EXE-embedded icon (set via <ApplicationIcon> in the csproj).
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }
}
