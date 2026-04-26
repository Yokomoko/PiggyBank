using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Analytics;
using PiggyBank.App.Dashboard;
using PiggyBank.App.Debts;
using PiggyBank.App.Joint;
using PiggyBank.App.Pockets;
using PiggyBank.App.Settings;
using Wpf.Ui.Controls;

namespace PiggyBank.App;

public partial class MainWindow : FluentWindow, INotifyPropertyChanged
{
    private readonly IServiceProvider _services;
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

    public MainWindow(CurrentMonthView currentMonth, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = this;
        RootContent.Content = currentMonth;
        _services = services;
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
