using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Pockets;

namespace PiggyBank.App.Dashboard;

public partial class CurrentMonthView : UserControl
{
    // ui:NumberBox.Value only updates on LostFocus/Enter, which means the
    // bound NewOutgoingAmount / NewSpendAmount don't change while the user
    // is typing — so CanAddOutgoing/CanAddSpend stays stale and the Add
    // button behaves one keystroke (or worse, one focus-lose) behind.
    // This guard prevents the VM-set → binding → NumberBox.Text → TextChanged
    // loop from re-entering.
    private bool _suppressAmountTextChanged;

    public CurrentMonthView()
    {
        InitializeComponent();
    }

    public CurrentMonthView(CurrentMonthViewModel vm, IServiceProvider services) : this()
    {
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            if (DataContext is CurrentMonthViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };

        vm.RecordSavingsRequested += async (_, amount) =>
        {
            var window = services.GetRequiredService<RecordDepositWindow>();
            window.Owner = System.Windows.Application.Current.MainWindow;
            if (window.DataContext is RecordDepositViewModel depositVm)
                depositVm.Amount = amount;
            var ok = window.ShowDialog();
            if (ok == true)
                await vm.LoadCommand.ExecuteAsync(null);
        };

        // Ctrl+N focuses the quick-add payee box so the user can type
        // straight into a spend entry without reaching for the mouse.
        InputBindings.Add(new KeyBinding(
            new RelayKeyCommand(() => QuickAddPayeeBox.Focus()),
            new KeyGesture(Key.N, ModifierKeys.Control)));
    }

    /// <summary>Per-keystroke push: parses the NumberBox text and writes it
    /// straight to the bound VM property. Empty / unparseable input maps to
    /// 0, so clearing the box correctly disables the Add button.</summary>
    private void OnAmountBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAmountTextChanged) return;
        if (sender is not Wpf.Ui.Controls.NumberBox box) return;
        if (DataContext is not CurrentMonthViewModel vm) return;

        var text = box.Text;
        var value = 0m;
        if (!string.IsNullOrWhiteSpace(text))
        {
            decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        _suppressAmountTextChanged = true;
        try
        {
            switch (box.Name)
            {
                case "AddOutgoingAmountBox": vm.NewOutgoingAmount = value; break;
                case "QuickAddAmountBox":    vm.NewSpendAmount    = value; break;
            }
        }
        finally { _suppressAmountTextChanged = false; }
    }
}

internal sealed class RelayKeyCommand(Action action) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
