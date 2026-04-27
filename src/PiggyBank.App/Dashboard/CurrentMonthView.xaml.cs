using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Notifications;
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
        var categoryNotifier = services.GetRequiredService<CategoryChangeNotifier>();

        // Subscribe in Loaded / unsubscribe in Unloaded so transient view
        // instances don't leak — the notifier is a singleton, so a stale
        // reference here would keep the whole VM graph alive after the
        // user navigates to another tab.
        EventHandler categoryHandler = async (_, _) =>
        {
            if (DataContext is CurrentMonthViewModel current)
                await current.RefreshCategoriesCommand.ExecuteAsync(null);
        };

        Loaded += async (_, _) =>
        {
            if (DataContext is CurrentMonthViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
            categoryNotifier.Changed += categoryHandler;
        };

        Unloaded += (_, _) => categoryNotifier.Changed -= categoryHandler;

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

    /// <summary>Selects all text in the NumberBox when it gains keyboard
    /// focus — so tabbing into the Amount box (which holds a placeholder
    /// "0") lets the user type the amount directly without first deleting.
    /// Wired on the underlying TextBox via the GotKeyboardFocus event so
    /// it survives both tab-in and click-in.</summary>
    private void OnAmountBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.NumberBox box) return;
        // The NumberBox hosts a TextBox internally; SelectAll on the box
        // dispatcher-delays so the framework's focus-set logic doesn't
        // immediately reset the selection on entry.
        box.Dispatcher.BeginInvoke(new Action(box.SelectAll),
            System.Windows.Threading.DispatcherPriority.Input);
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

    /// <summary>Per-keystroke push for the existing-row Amount fields
    /// (outgoings list + ledger transaction list). The default WPF-UI
    /// NumberBox only commits its Value on LostFocus, which means the
    /// bound row's Amount stays stale while the user is typing — so the
    /// Recalculate that hangs off PropertyChanged doesn't fire and the
    /// surplus / running balance don't update until tab-out. Pushing
    /// per-keystroke directly into the row makes edits feel live.</summary>
    private void OnRowAmountTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAmountTextChanged) return;
        if (sender is not Wpf.Ui.Controls.NumberBox box) return;

        var text = box.Text;
        var value = 0m;
        if (!string.IsNullOrWhiteSpace(text))
        {
            decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        _suppressAmountTextChanged = true;
        try
        {
            switch (box.DataContext)
            {
                case MonthlyOutgoingRow outgoing: outgoing.Amount = value; break;
                case TransactionRow tx:           tx.Amount       = value; break;
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
