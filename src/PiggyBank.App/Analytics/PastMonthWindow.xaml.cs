using Wpf.Ui.Controls;

namespace PiggyBank.App.Analytics;

public partial class PastMonthWindow : FluentWindow
{
    public PastMonthWindow(PastMonthViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }
}
