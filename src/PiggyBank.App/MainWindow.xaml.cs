using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Analytics;
using PiggyBank.App.Dashboard;
using PiggyBank.App.Debts;
using PiggyBank.App.Joint;
using PiggyBank.App.Pockets;
using PiggyBank.App.Settings;
using PiggyBank.App.Updates;
using Wpf.Ui.Controls;

namespace PiggyBank.App;

public partial class MainWindow : FluentWindow, INotifyPropertyChanged
{
    private readonly IServiceProvider _services;
    private readonly UpdateService _updates;
    private string _profileHeading = "";
    private string _profileColour = "#00000000";

    public string ProfileHeading
    {
        get => _profileHeading;
        set { _profileHeading = value; PropertyChanged?.Invoke(this, new(nameof(ProfileHeading))); }
    }

    /// <summary>Hex string bound through <c>HexToBrushConverter</c> in XAML —
    /// keeps the MainWindow shell free of WPF Brush types, which makes the
    /// eventual ShellViewModel trivially portable.</summary>
    public string ProfileColour
    {
        get => _profileColour;
        set { _profileColour = value; PropertyChanged?.Invoke(this, new(nameof(ProfileColour))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(CurrentMonthView currentMonth, IServiceProvider services, UpdateService updates)
    {
        InitializeComponent();
        DataContext = this;
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

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }
}
