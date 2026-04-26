using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PiggyBank.App.Settings;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.ThemeChanged += (_, theme) => ApplyTheme(theme);

        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }

    internal static void ApplyTheme(string theme)
    {
        var applicationTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown,  // "System" — lepoco follows OS
        };
        ApplicationThemeManager.Apply(applicationTheme);
    }
}
