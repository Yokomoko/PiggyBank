using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace PiggyBank.App.Analytics;

public partial class AnalyticsView : UserControl
{
    public AnalyticsView()
    {
        InitializeComponent();
    }

    public AnalyticsView(AnalyticsViewModel vm, IServiceProvider services) : this()
    {
        DataContext = vm;

        vm.ViewMonthRequested += (_, monthRow) =>
        {
            var window = services.GetRequiredService<PastMonthWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            if (window.DataContext is PastMonthViewModel pastVm)
                pastVm.MonthId = monthRow.MonthId;
            window.ShowDialog();
        };

        Loaded += async (_, _) =>
        {
            if (DataContext is AnalyticsViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };
    }
}
