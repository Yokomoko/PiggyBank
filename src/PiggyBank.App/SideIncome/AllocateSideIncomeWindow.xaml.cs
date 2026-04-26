using Wpf.Ui.Controls;

namespace PiggyBank.App.SideIncome;

public partial class AllocateSideIncomeWindow : FluentWindow
{
    public AllocateSideIncomeWindow(AllocateSideIncomeViewModel vm)
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
