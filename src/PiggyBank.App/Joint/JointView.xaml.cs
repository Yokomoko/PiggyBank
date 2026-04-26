using System.Windows;
using System.Windows.Controls;

namespace PiggyBank.App.Joint;

public partial class JointView : UserControl
{
    public JointView()
    {
        InitializeComponent();
    }

    public JointView(JointViewModel vm) : this()
    {
        DataContext = vm;

        Loaded += async (_, _) =>
        {
            if (DataContext is JointViewModel current)
                await current.LoadCommand.ExecuteAsync(null);
        };
    }

    /// <summary>Header "Add account" button toggles the inline add-account
    /// card so users with one or more existing accounts can still create
    /// another without leaving the screen.</summary>
    private void OnAddAccountClicked(object sender, RoutedEventArgs e)
    {
        AddAccountCard.Visibility = AddAccountCard.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
