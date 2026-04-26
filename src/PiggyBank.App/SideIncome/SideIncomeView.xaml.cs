using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.Core.Entities;

namespace PiggyBank.App.SideIncome;

public partial class SideIncomeView : UserControl
{
    public SideIncomeView()
    {
        InitializeComponent();
    }

    /// <summary>Handle the template-picker selection so the VM can pre-fill
    /// the add-entry form. Reset the ComboBox after so it can be re-picked
    /// (otherwise the user has to click "Apply" twice for the same template).</summary>
    private void OnTemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SideIncomeViewModel vm) return;
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not SideIncomeTemplate template) return;
        vm.ApplyTemplateCommand.Execute(template);
        cb.SelectedItem = null;  // allow re-selecting the same template later
    }

    public SideIncomeView(SideIncomeViewModel vm, IServiceProvider services) : this()
    {
        DataContext = vm;

        vm.AllocateRequested += async (_, entryRow) =>
        {
            var window = services.GetRequiredService<AllocateSideIncomeWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            if (window.DataContext is AllocateSideIncomeViewModel allocVm)
            {
                allocVm.EntryId = entryRow.Id;
                allocVm.EntryPaidOn = entryRow.PaidOn;
                allocVm.EntryDescription = entryRow.Description;
                allocVm.EntryRemaining = entryRow.Remaining;
                allocVm.MonthEntries = null;
            }
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        vm.AllocateMonthRequested += async (_, monthRow) =>
        {
            var window = services.GetRequiredService<AllocateSideIncomeWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            if (window.DataContext is AllocateSideIncomeViewModel allocVm)
            {
                // Month-mode: loop every entry with remaining. MonthEntries drives
                // the modal's copy and the per-entry allocation pass.
                allocVm.MonthEntries = monthRow.Entries
                    .Where(e => e.Remaining > 0m)
                    .ToList();
                allocVm.MonthLabel = monthRow.MonthLabel;
            }
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        Loaded += async (_, _) =>
        {
            if (DataContext is SideIncomeViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };
    }
}
