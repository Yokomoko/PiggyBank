using Wpf.Ui.Controls;

namespace PiggyBank.App.Analytics;

public partial class CompareMonthsWindow : FluentWindow
{
    public CompareMonthsWindow(CompareMonthsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);
    }
}
