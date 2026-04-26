using System.Windows;
using PiggyBank.App.Profiles;
using Wpf.Ui.Controls;

namespace PiggyBank.App.Views;

public partial class ProfilePickerWindow : FluentWindow
{
    public Guid? ChosenProfileId { get; private set; }
    public bool CreateRequested { get; private set; }

    public ProfilePickerWindow(ProfilePickerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.ProfileChosen += (_, id) =>
        {
            ChosenProfileId = id;
            DialogResult = true;
            Close();
        };

        vm.CreateProfileRequested += (_, _) =>
        {
            CreateRequested = true;
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }
}
