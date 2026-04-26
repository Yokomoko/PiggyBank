using Wpf.Ui.Controls;

namespace PiggyBank.App.Pockets;

public partial class ArchivePocketTransferWindow : FluentWindow
{
    public ArchivePocketTransferWindow(ArchivePocketTransferViewModel vm)
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

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
