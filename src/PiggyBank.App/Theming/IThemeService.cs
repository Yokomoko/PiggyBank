namespace PiggyBank.App.Theming;

/// <summary>
/// Applies a theme name ("System" / "Light" / "Dark") to the running app.
/// Abstracted so the shell doesn't reach into the Settings namespace for
/// a static helper (prior arch review flagged the static
/// <c>SettingsWindow.ApplyTheme</c> call from <c>App.xaml.cs</c> as a smell).
/// </summary>
public interface IThemeService
{
    void Apply(string theme);
}
