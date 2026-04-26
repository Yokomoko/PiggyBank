using Wpf.Ui.Appearance;

namespace PiggyBank.App.Theming;

public sealed class WpfUiThemeService : IThemeService
{
    public void Apply(string theme)
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
