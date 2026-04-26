using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace PiggyBank.App.Pockets;

public partial class PocketsView : UserControl
{
    public PocketsView()
    {
        InitializeComponent();
    }

    public PocketsView(PocketsViewModel vm, IServiceProvider services) : this()
    {
        DataContext = vm;

        vm.RecordDepositRequested += async (_, _) =>
        {
            var window = services.GetRequiredService<RecordDepositWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        vm.DepositToPocketRequested += async (_, target) =>
        {
            var window = services.GetRequiredService<RecordDepositWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            // Lock the window to this pocket before Loaded fires — LoadAsync
            // reads TargetPocketId to build the single-row preview and headline.
            if (window.DataContext is RecordDepositViewModel depositVm)
            {
                depositVm.TargetPocketId = target.Id;
                depositVm.TargetPocketName = target.Name;
            }
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        vm.ArchiveTransferRequested += async (_, source) =>
        {
            var window = services.GetRequiredService<ArchivePocketTransferWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            // Pass the source row through before Loaded fires — LoadAsync
            // reads SourcePocketId to build the transfer-target list (and to
            // omit the source from it).
            if (window.DataContext is ArchivePocketTransferViewModel archiveVm)
            {
                archiveVm.SourcePocketId = source.Id;
                archiveVm.SourcePocketName = source.Name;
                archiveVm.SourceBalance = source.CurrentBalance;
            }
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        Loaded += async (_, _) =>
        {
            if (DataContext is PocketsViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };
    }
}
