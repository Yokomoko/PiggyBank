using PiggyBank.App.Profiles;
using Wpf.Ui.Controls;

namespace PiggyBank.App.Views;

public partial class CreateProfileWindow : FluentWindow
{
    public Guid? CreatedProfileId { get; private set; }

    public CreateProfileWindow(CreateProfileViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.ProfileCreated += (_, id) =>
        {
            CreatedProfileId = id;
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }
}
