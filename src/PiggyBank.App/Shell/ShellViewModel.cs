using CommunityToolkit.Mvvm.ComponentModel;
using PiggyBank.Core.Entities;

namespace PiggyBank.App.Shell;

/// <summary>
/// Holds the chrome state for <see cref="MainWindow"/> — profile heading
/// text and the colour chip rendered next to it. Lifted out of the window
/// code-behind so the shell stays a thin host: layout in XAML, state on
/// the VM, no manual <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// plumbing on the window itself.
/// </summary>
/// <remarks>
/// Singleton — the running app has exactly one shell. <c>App.OnStartup</c>
/// resolves it once the active <see cref="Profile"/> is known and pushes
/// the heading / colour in.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private string _profileHeading = "";

    /// <summary>Hex colour string for the profile dot. Bound through the
    /// <c>HexToBrush</c> converter declared in <c>App.xaml</c> so the VM
    /// stays free of WPF Brush types.</summary>
    [ObservableProperty] private string _profileColour = "#00000000";

    /// <summary>Pushes the active profile's identity into the chrome.
    /// Pass null for the "no profile chosen yet" state.</summary>
    public void SetActiveProfile(Profile? profile)
    {
        ProfileHeading = profile is null ? "Welcome." : $"Signed in as {profile.DisplayName}.";
        ProfileColour = profile?.ColourHex ?? "#00000000";
    }
}
