using System.Windows.Controls;

namespace PiggyBank.App.Debts;

public partial class DebtsView : UserControl
{
    public DebtsView()
    {
        InitializeComponent();
    }

    public DebtsView(DebtsViewModel vm) : this()
    {
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            if (DataContext is DebtsViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };
    }
}
