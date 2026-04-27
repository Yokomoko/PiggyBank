namespace PiggyBank.App.Notifications;

/// <summary>
/// Fires when the set of categories has changed (added, archived, or
/// renamed) somewhere in the app — typically in the Settings dialog —
/// so views that hold category-aware dropdowns can refresh without
/// needing a full app restart.
/// </summary>
/// <remarks>
/// Singleton. Subscribers MUST unsubscribe on their view's Unloaded
/// event: the event handler holds a strong reference, and a
/// CurrentMonthView (transient) that's been navigated away from would
/// otherwise be kept alive by this list, taking its DbContext / VM
/// graph with it.
/// </remarks>
public sealed class CategoryChangeNotifier
{
    public event EventHandler? Changed;
    public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
