using Wpf.Ui.Controls;

namespace PiggyBank.App.Pockets;

public partial class RecordDepositWindow : FluentWindow
{
    public RecordDepositWindow(RecordDepositViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.Completed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }
}
