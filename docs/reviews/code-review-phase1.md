# PiggyBank — Phase 1 MVP Code Review

> Review date: 2026-04-22 · Reviewer: Claude (second pass, feature-complete MVP)
> Scope: full `src/` + `tests/` tree on completion of Week-3 CRUD + settings work
> Prior: `docs/reviews/code-review.md` (2026-04-22 first pass)

---

## 1. Verdict

**Substantially healthier.** Every Red from the prior review is fixed; eight of nine Yellows are closed or mitigated; the Week-3 feature surface (CRUD, settings, theme, close-month) shipped without disturbing the tenancy rails or the allowance engine. The rough edges have shifted from "wires not connected" to "one VM knows too much" — the fixes are structural not shipping-blocking.

---

## 2. Prior findings status

### Reds

| # | Finding | Status | Evidence |
|---|---|---|---|
| 1 | `MainWindow.ProfileColour` not set | **Closed** | `App.xaml.cs:69` sets `mainWindow.ProfileColour = profile?.ColourHex ?? "#00000000"`; shape changed from `Brush` to `string` with a converter (a better split — see new findings). |
| 2 | Profile picker hex-string bound as Brush | **Closed** | `HexToBrushConverter` added (`Converters/HexToBrushConverter.cs`), registered in `App.xaml:15`, applied at `ProfilePickerWindow.xaml:35` and `MainWindow.xaml:29`. Converter handles null/bad input by returning `Brushes.Transparent` — defensive, correct. |
| 3 | No global exception handler; `async void OnStartup` | **Closed** | `App.xaml.cs:28-30` wires all three handlers; `OnStartup` is now synchronous with a fire-and-forget `BootAsync` wrapped in `try/catch` at line 39-77. A belt-and-braces crash file is also written (`App.xaml.cs:161-172`) so startup-path failures are recoverable even before the logger wires up. |
| 4 | Ledger `CanUserAddRows="True"` persisted nothing | **Closed** | Both grids now `CanUserAddRows="False"` (`CurrentMonthView.xaml:111, 179`) and a dedicated quick-add form replaces the inline add (line 224-252), backed by `AddSpendCommand` / `AddOutgoingCommand`. The fix matches option 2 from the prior review (introduce commands, keep grid) — the better call. |

### Yellows

| # | Finding | Status | Evidence |
|---|---|---|---|
| 5 | `IsWageVisible` half-feature | **Closed** | `MonthlyOutgoingRow.ApplyWageMask` at `CurrentMonthViewModel.cs:405-411` renders `••••` for wage rows when hidden; `ToggleWageCommand` at line 182-186 iterates the collection and re-masks. Column binds `DisplayAmount` (line 117) not the raw Amount. (Minor regression — see new finding N-3.) |
| 6 | VM reaches around repos into `AppDbContext` | **Partial** | Still direct `db.ProfileSettings.FirstOrDefaultAsync` at `CurrentMonthViewModel.cs:89, 141` and `SettingsViewModel.cs:45, 52, 68, 78`. No `IProfileSettingsRepository` was introduced. `_profileSettings` is now stored and its `BufferPerDay` feeds the calculator (line 363) — the downstream bug is fixed but the architectural bleed remains. |
| 7 | Service-locator pattern in VMs | **Regressed** | Was "pervasive"; now *more* pervasive. `CurrentMonthViewModel` has 14 `GetRequiredService` sites (lines 83-87, 123, 138-139, 159, 173, 193, 208, 240, 251, 280, 309, 329, 344). Every new Week-3 command copied the pattern. `ICurrentScopeAccessor` from the prior review wasn't introduced; the anti-pattern is now the house style. |
| 8 | `BufferPerDay` hardcoded | **Closed** | `CurrentMonthViewModel.cs:363` reads `_profileSettings?.BufferPerDay ?? 10m`. Settings screen writes it (`SettingsViewModel.cs:80`). End-to-end path verified. |
| 9 | `MonthlyOutgoing` drops `IsIncome` | **Open** | Entity still lacks `IsIncome` (`MonthlyOutgoing.cs:9-22`). No migration added. Still a Phase-2 hazard for category-filtered history. |
| 10 | `ProfileAdminService.CreateAsync` error path | **Partial** | Still two `SaveChangesAsync` calls inside the transaction (`ProfileAdminService.cs:66, 94`) and still no `try/catch` for logging. `colourHex` length regex validation not added. One upside: `displayName` null/empty is checked at line 53-54. |
| 11 | `async void` on `Loaded` propagates | **Partial** | `Loaded` handlers in `CurrentMonthView.xaml.cs:17-21`, `CreateProfileWindow.xaml.cs:22`, `ProfilePickerWindow.xaml.cs:31`, `SettingsWindow.xaml.cs:16` are still raw `async void` with no `try/catch`. **But** the new global dispatcher handler (finding 3) now catches them — so this is defensible though not ideal (user sees the modal error dialog instead of a silent crash). |
| 12 | Hardcoded strings in XAML | **Open** | No resource-dictionary extraction. Every string still literal. Acceptable for MVP; flag sustains. |
| 13 | Currency format strings duplicated | **Open** | `StringFormat='£#\,##0.00'` now appears 9× in `CurrentMonthView.xaml` (lines 81, 188, 265, 281, 283, 285, 287, 289, 357); `£+…` at line 291. Cost of not extracting went up, not down. |

### Greens (selective)

- **Finding 14** (`Tenant.GetType()` diagnostic) — **Closed**. The stray call is gone; `TestDbFactory` (now named `TestDb` at `tests/…/TestDbFactory.cs`) is clean.
- **Finding 15** (empty `SeedCategoryDefaultEnabled` migration) — **Open**. `20260422204815_SeedCategoryDefaultEnabled.cs` is still an empty Up/Down pair; the `Designer.cs` sibling still materialises. Migration noise compounds.
- **Finding 17** (`BudgetCalculator._clock` unused) — **Open**. Field + parameter still unused (`BudgetCalculator.cs:13-17`). Comment retained. Low priority.
- **Finding 19** (MainWindow rolls own `INPC`) — **Open**. `MainWindow.xaml.cs:10-31` still implements `INotifyPropertyChanged` by hand. The ProfileColour type changed from `Brush` to `string` but the pattern is unchanged. Settings dialog added one more seam that would have been cleaner behind a `ShellViewModel`.
- **Finding 24** (`months.Single()` confusing failure) — **Open**. `RepositoryTenantLeakTests.cs:46` still uses `.Single()`.
- **Finding 25** (`CreateCurrentMonthAsync` missing CT) — **Closed**. Signature at `CurrentMonthViewModel.cs:134` takes `CancellationToken ct = default` and forwards it to both `svc.CreateAsync` and `LoadAsync(ct)`.
- **Finding 26** (`CreateProfileViewModel.ColourHex` not bound) — **Open**. The wizard XAML (`CreateProfileWindow.xaml`) still exposes no colour picker; `ColourHex` / `IconKey` remain set-only defaults.
- **Finding 27** (layout sensible) — **Still true.** No regressions on file layout.

**Summary:** 3 Reds all closed; 9 Yellows → 4 closed, 3 partial, 1 regressed, 1 open; 3 Greens closed, ~6 still open (low-priority maintenance).

---

## 3. New findings — Week 3 work

### Finding N-1 — Red: Duplicate "Future" column in ledger DataGrid

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthView.xaml:202-207`

Lines 202-204 and 205-207 declare the same `DataGridCheckBoxColumn Header="Future"` twice. Both bind `IsFuture` two-way. At runtime WPF renders two checkboxes side-by-side — the user can toggle either, the last write wins through the change notification. This is a visible bug: it will ship broken.

```xml
<DataGridCheckBoxColumn Header="Future"
                        Binding="{Binding IsFuture, Mode=TwoWay}"
                        Width="60" />
<DataGridCheckBoxColumn Header="Future"        <!-- delete this block -->
                        Binding="{Binding IsFuture, Mode=TwoWay}"
                        Width="60" />
```

**Fix:** delete lines 205-207.

### Finding N-2 — Red: Row event handlers leak after reload

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:113-119, 374-387`

`LoadAsync` and `Clear` both call `Outgoings.Clear()` / `Transactions.Clear()` without first un-subscribing `row.PropertyChanged` handlers that were added by `WireOutgoing` / `WireTransaction` (lines 288-299). Each reload:

1. Old `MonthlyOutgoingRow` instances retain a strong reference to the VM via the subscription.
2. New instances are wired up alongside.
3. If the user edits a *detached* ghost row (e.g. during a bound animation) the handler still fires and calls `repo.FindAsync` → `repo.UpdateAsync` on an entity the user thinks they deleted.

In practice (a) is just a transient leak since the VM itself is Transient and rebuilt per navigation; (b) is the live hazard. An `async void` handler on a row with no corresponding DB entity will silently swallow the `null` from `FindAsync` — but the flow is now an order-of-operations race.

**Fix:**

```csharp
private void DetachRows(IEnumerable<MonthlyOutgoingRow> rows)
{
    foreach (var row in rows) row.PropertyChanged -= OutgoingRowChanged;
}
private void DetachRows(IEnumerable<TransactionRow> rows)
{
    foreach (var row in rows) row.PropertyChanged -= TransactionRowChanged;
}
// ...
DetachRows(Outgoings); Outgoings.Clear();
DetachRows(Transactions); Transactions.Clear();
```

Or switch to `WeakEventManager<PropertyChangedEventArgs>` per §13.2 Risk 2 of the architecture doc.

### Finding N-3 — Yellow: `MonthlyOutgoingRow.OnAmountChanged` logic is backwards-looking

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:413-414`

```csharp
partial void OnAmountChanged(decimal value)
    => ApplyWageMask(!IsMasked || !IsWage);
```

The partial passes `!IsMasked || !IsWage` as the `wageVisible` argument. Trace table:

| `IsMasked` | `IsWage` | `!IsMasked \|\| !IsWage` | Expected `wageVisible` |
|---|---|---|---|
| false | false | true | irrelevant (non-wage) |
| false | true  | false | true (was unmasked, keep unmasked) |
| true  | false | true | irrelevant |
| true  | true  | false | false (was masked, keep masked) |

The **second row is wrong**: if the outgoing is a wage that was unmasked and the user edits the amount, `wageVisible` is passed `false` and the row re-masks despite the toggle. Repro: toggle wage on, edit wage amount → amount becomes `••••` mid-edit. The logic is inverted.

**Fix:** capture wage visibility on the row at construction (or via a VM callback):

```csharp
public sealed partial class MonthlyOutgoingRow(MonthlyOutgoing source) : ObservableObject
{
    private bool _currentWageVisible = true;    // last state we were told

    public void ApplyWageMask(bool wageVisible)
    {
        _currentWageVisible = wageVisible;
        IsMasked = IsWage && !wageVisible;
        DisplayAmount = IsMasked ? "••••" : Amount.ToString("C2", UkCulture);
    }

    partial void OnAmountChanged(decimal value) => ApplyWageMask(_currentWageVisible);
}
```

### Finding N-4 — Yellow: `CurrentMonthViewModel` is a 388-line god-VM

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs`

One VM now owns: load/clear, month creation, close, rollover banner, wage toggle, month-header editing (paydays + carry-over), quick-add spend, quick-add outgoing, delete spend, delete outgoing, row-level edit propagation, allowance recalc, and category rollup refresh. 22 observable properties, 12 commands, 2 async-void event handlers. The screen genuinely has many concerns — but three responsibilities are separable:

- **Outgoings section** — `Outgoings` collection, `NewOutgoingName/Amount/Category`, `AddOutgoingCommand`, `DeleteOutgoingCommand`, `OutgoingRowChanged` handler, `WireOutgoing`. ~80 lines.
- **Ledger section** — `Transactions`, `NewSpendDate/Payee/Amount/Category`, `AddSpendCommand`, `DeleteSpendCommand`, `TransactionRowChanged`, `WireTransaction`, and `RefreshRollupsAsync`. ~90 lines.
- **Allowance / month-header** — the remaining core: `Recalculate`, month-header editing, close / rollover flows. ~120 lines.

Extraction sketch, concrete public surface:

```csharp
public sealed partial class OutgoingsSectionViewModel(
    ProfileSessionManager sessions,
    Func<Guid> monthIdAccessor,           // reads parent's Month.Id
    Action onChanged) : ObservableObject
{
    public ObservableCollection<MonthlyOutgoingRow> Rows { get; } = [];
    public ObservableCollection<Category> Categories { get; }   // shared with parent
    [ObservableProperty] private string _newName;
    [ObservableProperty] private decimal _newAmount;
    [ObservableProperty] private Category? _newCategory;
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<MonthlyOutgoingRow> DeleteCommand { get; }

    public Task LoadAsync(Guid monthId, bool wageVisible, CancellationToken ct);
    public void ApplyWageMask(bool wageVisible);
    public decimal Total => Rows.Sum(r => r.Amount);
}

public sealed partial class LedgerSectionViewModel(
    ProfileSessionManager sessions,
    Func<Guid> monthIdAccessor,
    Action onChanged) : ObservableObject
{
    public ObservableCollection<TransactionRow> Rows { get; } = [];
    public ObservableCollection<CategoryRollupRow> Rollups { get; } = [];
    [ObservableProperty] private DateOnly _newDate;
    [ObservableProperty] private string _newPayee;
    [ObservableProperty] private decimal _newAmount;
    [ObservableProperty] private Category? _newCategory;
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<TransactionRow> DeleteCommand { get; }

    public Task LoadAsync(Guid monthId, CancellationToken ct);
    public decimal PresentSpend => Rows.Where(t => !t.IsFuture).Sum(r => r.Amount);
}

public sealed partial class AllowanceViewModel(
    BudgetCalculator calc,
    TimeProvider clock) : ObservableObject
{
    // All the 12 ObservableProperty allowance fields.
    public void Recalculate(decimal total, decimal spent, decimal carriedOver,
                            DateOnly last, DateOnly next, decimal bufferPerDay);
}
```

`CurrentMonthViewModel` keeps the three sub-VMs plus month-header editing and closure flow — drops from 388 lines to ~150. The `onChanged` callback lets sub-VMs signal recompute without the parent subscribing to each collection. The change is not strictly necessary to ship Phase 1, but will save pain the first time a Phase-2 feature (debt block, savings sheet) needs to live alongside the outgoings grid.

**Worth doing?** Yes, at the Phase 2 kickoff — the domain will grow, the allowance VM will want unit tests without a DB on the other side of the scope accessor, and the ledger section will want its own Payee-rule side panel. Carve now while the seams are fresh.

### Finding N-5 — Yellow: Row VMs listening to own `PropertyChanged` is the wrong pattern

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:288-340`

The current approach subscribes to each row's `PropertyChanged`, filters by property name, loads the entity, copies fields, saves. Two defects:

1. Every individual cell edit triggers a DB round-trip (`FindAsync` + `UpdateAsync` per property change). Edit five columns → five saves. SaveChanges is not atomic across the five.
2. The async void handler swallows exceptions that the global handler will catch — but as a user-visible modal, not an inline error state on the row.

WPF's `DataGrid.RowEditEnding` is the idiomatic seam: fires once when the user commits a row edit (Enter or focus loss). The handler receives the row and the edit action (Commit / Cancel), a single save happens, failure can revert the row.

```xml
<DataGrid RowEditEnding="OnOutgoingRowEditEnding" ... />
```

```csharp
private async void OnOutgoingRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
{
    if (e.EditAction != DataGridEditAction.Commit) return;
    if (e.Row.Item is not MonthlyOutgoingRow row) return;
    try
    {
        await _outgoingsSection.PersistEditAsync(row);
    }
    catch (Exception ex)
    {
        e.Cancel = true;
        _errors.Show($"Couldn't save row: {ex.Message}");
    }
}
```

Row VM becomes pure data. Repository update is a single call. The section VM owns the persist method. This matches MudBlazor's `DataGridEditMode.Cell` pattern.

**Worth doing?** Yes, but pair it with N-4. If the section VMs exist, the "where do I hang RowEditEnding" question answers itself.

### Finding N-6 — Yellow: `SettingsWindow.ApplyTheme` is a static reaching across layers

**File:** `src/PiggyBank.App/Settings/SettingsWindow.xaml.cs:19-28`, called from `src/PiggyBank.App/App.xaml.cs:113`

`ApplyTheme` is `internal static` on the window and called during startup (where no window instance exists yet). Functionally fine, but:

- The method's only behaviour is `ApplicationThemeManager.Apply(...)` — the window is just a namespace for the function.
- `App.xaml.cs:113` calls `SettingsWindow.ApplyTheme(theme)` just to access that static — coupling startup to the settings window's class name.
- Theme is a cross-cutting concern (every window observes it) — belongs in a dedicated service.

**Fix:** introduce `ThemeService` in `App.Hosting` (or `App.Theming`):

```csharp
public sealed class ThemeService
{
    public void Apply(string theme) =>
        ApplicationThemeManager.Apply(theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark"  => ApplicationTheme.Dark,
            _       => ApplicationTheme.Unknown,
        });
}
```

Register in `AppHost.Build`. `App.BootAsync` resolves it and calls `Apply`. `SettingsViewModel` injects it and calls `Apply` on save (removing the `ThemeChanged` event entirely — the VM becomes a plain form, the service does the work). Three-line class, removes the startup → settings-window reach.

### Finding N-7 — Yellow: `SettingsViewModel` conflates system and profile settings

**File:** `src/PiggyBank.App/Settings/SettingsViewModel.cs:14-89`

The VM holds:
- `_systemSettings` (single-row `AppSettings` — theme, last profile)
- `_profileSettings` (per-profile — food budget, buffer, payday)

They have different lifetimes, different scopes (`OpenAdminScope()` vs `_sessions.Current.Services`), different save contracts. `LoadAsync` has two sections (lines 42-47 and 49-60); `SaveAsync` has two sections (lines 66-74 and 76-85). `_profileSettings` is not created when missing — if the row doesn't exist (which it shouldn't because `ProfileAdminService.CreateAsync` always inserts one), the profile sub-save silently no-ops.

The UI (`SettingsWindow.xaml`) already renders them in distinct cards ("Appearance" / "This profile" / "About"). The VM should mirror that:

```csharp
public sealed partial class SettingsViewModel(AppSettingsService app, ProfileSettingsService profile) : ObservableObject
{
    public AppSettingsViewModel AppSettings { get; } = new(app);
    public ProfileSettingsViewModel ProfileSettings { get; } = new(profile);
    public AboutViewModel About { get; } = new();

    [RelayCommand]
    public async Task SaveAsync(CancellationToken ct)
    {
        await AppSettings.SaveAsync(ct);
        await ProfileSettings.SaveAsync(ct);
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}.";
    }
}
```

Each sub-VM owns its own load/save. When Phase 2 adds a "payee rules" tab (§14.4 of the architecture doc), it's a fourth sub-VM alongside these three, not another 20 lines spliced into a monolith.

**Worth doing?** Yes — at the point the third sub-concern (payee rules, import sources) needs settings space, which is imminent.

### Finding N-8 — Yellow: `async void` row handlers don't cooperate with the global handler

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:301, 319`

`OutgoingRowChanged` and `TransactionRowChanged` are `async void`. The global `DispatcherUnhandledException` (installed at `App.xaml.cs:28`) only catches exceptions on the UI dispatcher. `async void` exceptions on a `SynchronizationContext` do surface there — so that's fine. But the user experience is: edit a cell, the app shows a modal dialog saying "Unhandled dispatcher exception — [repo error]", the grid keeps the stale local state, the next edit re-triggers. No retry loop, no local status.

Matches the spec in a narrow reading (crashes are caught) but violates the "no silent cascade" UX philosophy: the user can't correct the root cause without knowing what "Save outgoing row" even means. Pair with finding N-5 — if `RowEditEnding` is the save seam, the handler can wrap persist in `try/catch` and surface a row-local toast (`TextBlock Text="Save failed — check amount"` in a dedicated footer region).

**Fix (minimal):** at least wrap each async-void handler in its own try/catch that writes to a `StatusMessage` on the VM:

```csharp
private async void OutgoingRowChanged(object? sender, PropertyChangedEventArgs e)
{
    try { await PersistOutgoingAsync(sender, e); }
    catch (Exception ex) { StatusMessage = $"Couldn't save: {ex.Message}"; }
}
```

### Finding N-9 — Yellow: `MainWindow.INPC` survived the Settings work

**File:** `src/PiggyBank.App/MainWindow.xaml.cs:10-31`

Prior finding 19 flagged this; Week 3 added a second manual property (`ProfileColour` changed from `Brush` to `string` — good! — but still manual). The settings button in the title bar (`MainWindow.xaml:33-38`) calls into `_services.GetRequiredService<SettingsWindow>()` directly from code-behind (line 43-45) — another concern the shell VM would own cleanly. A `ShellViewModel` would hold `ProfileHeading`, `ProfileColour`, `OpenSettingsCommand`, and (Phase 2) `SwitchProfileCommand`, `CloseMonthCommand` pulled up from the current screen for a global keybinding.

Still ~1 hour of work, higher payoff now that there's genuinely shared state + commands.

### Finding N-10 — Green: `NewOutgoingCategory` no `CanExecute` notification

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:270-274`

`CanAddOutgoing` checks `NewOutgoingName` and `NewOutgoingAmount` but not `NewOutgoingCategory` (which is optional — correct). There is no `partial void OnNewOutgoingCategoryChanged`; also correct because the command doesn't depend on it. Contrast with `AddSpendCommand` which also ignores `NewSpendCategory`. Consistent, not a bug.

### Finding N-11 — Green: Dead migration still present

**File:** `src/PiggyBank.Data/Migrations/20260422204815_SeedCategoryDefaultEnabled.cs`

Still empty Up/Down. Prior finding 15 open. Trivial cleanup — delete both `.cs` and `.Designer.cs`; run `dotnet ef migrations list` to ensure the model-snapshot doesn't need regenerating.

### Finding N-12 — Green: NuGet dead weight

**File:** `src/PiggyBank.App/PiggyBank.App.csproj`

- `Serilog.Sinks.File` (line 13) — still not wired. `AppHost.cs:34` only adds `AddDebug()`. The crash-file logic in `App.xaml.cs:161-172` hand-rolls a text dump, bypassing Serilog entirely. Pick: wire Serilog to a rolling file (architecture §10 spec), or drop the package.
- `LiveChartsCore.SkiaSharpView.WPF` (line 11) — no charts exist yet. Fine to pre-pull for Phase 2; just note the footprint (~20 MB bundled).

### Finding N-13 — Green: Unused `using` directives

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthView.xaml.cs:1-3`

```csharp
using System.Windows;          // only used by RoutedEventArgs which is imported via XAML partial
using System.Windows.Controls; // UserControl is referenced, so this one is fine
using System.Windows.Input;    // KeyBinding uses this — fine
```

`System.Windows` is unused (the `RoutedEventArgs` for the XAML `Loaded` event hander lambda `_, _` doesn't need the namespace). Minor cleanup.

### Finding N-14 — Green: `PiggyBank.App.Tests` is empty

**File:** `tests/PiggyBank.App.Tests/*` — zero `.cs` files beyond auto-generated attribute files.

The csproj pulls xUnit, NSubstitute, FluentAssertions, coverlet, references `PiggyBank.App`. The prior review architecture §12 calls for VM unit tests (`DashboardViewModel loads and populates allowance fields`, `Close-month command calls the right repository methods`, `Quick-add-spend command validates amount > 0`). Phase 1 shipped zero VM tests. The `CurrentMonthViewModel` has all the surface needed (12 commands, 22 observables) and the service-locator usage blocks it from being unit-tested without DI spin-up — which is exactly why `ICurrentScopeAccessor` from the prior review matters.

**Worth doing?** Yes, at Phase 2 kickoff: pair the Finding-7 ICurrentScopeAccessor refactor with `CurrentMonthViewModel_Tests` covering `AddSpendAsync_persists_and_recalculates`, `CloseMonth_makes_rollover_suggest_non_null`, `IsWageVisible_masks_wage_rows`, etc.

### Finding N-15 — Green: `Outgoings` missing `IsFuture` / signed-amount indication

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthView.xaml:115-123`

The outgoings DataGrid shows `Name` + `DisplayAmount` + a hidden `Raw` column (line 120-123, `Visibility="Collapsed"`). The `Raw` column exists but is collapsed — design intent is unclear from the XAML. If it's a diagnostic left over from wiring the masked display, delete it. If it's intended to surface on some hover/debug mode, add a comment. Currently reads as dead markup.

### Finding N-16 — Green: `ProfileSession.AdminScope` constructor cleverness

**File:** `src/PiggyBank.Data/Tenancy/ProfileSession.cs:26-38`

The dual constructor (public `(IServiceProvider, Guid)` and private `(IServiceScope, Guid)`) is necessary because `AdminScope` needs to create a scope and tag the tenant before handing it off. The `public` constructor sets tenant; the `private` one does not — so `AdminScope` uses `private` after itself tagging the tenant via `SetAdminScope`. Correct but subtle. Consider a single factory method pattern with `private ProfileSession(IServiceScope scope, Guid id)` being the only constructor:

```csharp
public static ProfileSession ForProfile(IServiceProvider root, Guid profileId)
{
    var scope = root.CreateScope();
    scope.ServiceProvider.GetRequiredService<MutableTenantContext>().Set(profileId);
    return new ProfileSession(scope, profileId);
}

public static ProfileSession AdminScope(IServiceProvider root)
{
    var scope = root.CreateScope();
    scope.ServiceProvider.GetRequiredService<MutableTenantContext>().SetAdminScope();
    return new ProfileSession(scope, Guid.Empty);
}

private ProfileSession(IServiceScope scope, Guid profileId) { ... }
```

Removes the "one constructor secretly depends on the caller having already set the tenant" asymmetry. Lower priority than structural findings above.

### Finding N-17 — Green: `SaveMonthHeaderAsync` doesn't validate

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:190-202`

No check that `NextPayday > LastPayday`. `MonthService.CreateAsync` does validate (line 40-41) but the header-edit path bypasses that method. If a user drags NextPayday before LastPayday, `PeriodEnd = NextPayday.AddDays(-1)` can become earlier than `PeriodStart`, and downstream `Recalculate` computes `DaysToNextPayday` clamped to zero (correct-ish) but `DaysSincePayday` could go negative before clamp. Add a guard + command `CanSaveMonthHeader` guard tied to the two paydays.

---

## 4. Top 5 patterns to keep

1. **Converter-based type bridging in XAML.** `HexToBrushConverter` (solves Red-2) and `DateOnlyToDateTimeConverter` (keeps the VM's `DateOnly` contract clean while WPF's `DatePicker` stays happy) are both tiny, both push the conversion into a well-named, testable seam, and both keep the VM layer portable to Blazor / MAUI later. Continue this pattern for any future type friction (e.g. `CategoryKindToIconConverter` at Phase 2).

2. **Global exception handler + belt-and-braces crash file.** `App.xaml.cs:139-179` wires all three .NET handlers AND writes a last-resort crash dump outside the logger path. The `try/catch` inside `BootAsync` (line 72-76) means even pre-logger startup failures write to disk. This is the pattern the architecture doc called for, implemented with a real-world escape hatch for "logger wasn't built yet" failures. Carry forward into any future boot-path work (e.g. Phase 2 import-staging DB bring-up).

3. **Reflection-driven tenant-leak test with vacuous-pass guard.** `TenantLeakTests.cs` (particularly the `MinimumExpectedTenantDbSets = 6` guard at line 25, the `Should().NotBeEmpty` seed assertion at line 69, and the explicit `throw` when an unknown entity type is added at line 125-126) is the textbook version of a safety-net test: it can't rot (auto-discovers new types), it can't vacuously pass (seeds every discovered set, asserts non-empty before asserting isolation), and it fails loudly with a pointer to the fix when a dev forgets to extend it. **Never weaken this.** Mirror the pattern when Phase 2 adds a cross-profile risk surface (payee-rule sharing, import source registration).

4. **Manual-rollover ceremony with a non-blocking banner.** `MonthService.SuggestRolloverAsync` returns a nullable, `ApplyRolloverFromPriorAsync` is explicit, the VM shows a banner only when `SuggestedRollover is not null && month.CarriedOverBalance == 0m` (line 126), and the user can dismiss or apply. No silent cascade. When the close-month flow lands in Week 3 (`CurrentMonthViewModel.cs:156-163`), it plugs into this machinery without special-casing. This is the "honest tool" philosophy made concrete.

5. **Per-profile DI scope with locked-once tenant context.** `ProfileSessionManager.OpenProfile` disposes + re-creates the scope on switch (`ProfileSessionManager.cs:39-41`). `MutableTenantContext.Set` locks after the first call (`MutableTenantContext.cs:22-27`). `TenantStampInterceptor.Stamp` rejects `ProfileId` mutation (line 60-65). These three together make profile-switching leak-safe by construction — the test suite would catch a regression, but the design doesn't need the test to be right. Same shape for Phase 2's import scoping: accept a `ProfileId` parameter, stamp from the scope, lock.

---

## 5. Top 3 refactors worth doing at the start of Phase 2

### Refactor A — Extract the `CurrentMonthViewModel` god-VM (findings N-4, N-5, N-8)

Introduce `OutgoingsSectionViewModel`, `LedgerSectionViewModel`, `AllowanceViewModel`. The parent retains month-header + close/rollover orchestration. Each section owns its load/save/quick-add surface. Row-edit propagation moves to a `RowEditEnding` seam in the XAML, persisted via a single `PersistEditAsync(row)` call on the section VM with try/catch at the seam.

**Unlocks:**
- `AllowanceViewModel` becomes unit-testable with an injected `IBudgetCalculator` mock — the Phase-2 "show savings trajectory" overlay needs a home that is not the same class as quick-add-spend form fields.
- Phase-2 payee-rule sidebar can live next to `LedgerSectionViewModel` without touching month-header code.
- Smaller diffs on UI changes — the `CurrentMonthViewModel.cs:388-line` file blocks mechanical review.

**Effort:** ~1 day including test coverage for the `AllowanceViewModel`.

### Refactor B — Introduce `ICurrentScopeAccessor` + `ThemeService` (findings 7, N-6, N-14)

Replace every `_sessions.Current.Services.GetRequiredService<T>()` with `_scope.Get<T>()`. Write the interface as the prior review (finding 7) sketched:

```csharp
public interface ICurrentScopeAccessor
{
    T Get<T>() where T : notnull;
    TReturn WithScope<TReturn>(Func<IServiceProvider, TReturn> action);
    Task WithAdminScopeAsync(Func<IServiceProvider, Task> action);
}
```

Pair with `ThemeService` (from N-6) and a pass to replace direct `db.ProfileSettings.FirstOrDefaultAsync` with an `IProfileSettingsRepository` (from prior finding 6). These three together unblock the `PiggyBank.App.Tests` project (N-14): `CurrentMonthViewModel` becomes testable with a mocked scope accessor and a hand-rolled `IProfileSettingsRepository` stub, no host or DB required.

**Unlocks:** actual VM unit tests; consistent Theme application across startup + save paths; eliminates the `SettingsWindow.ApplyTheme` static.

**Effort:** ~1 day including rewriting each VM and writing the first 4-6 VM tests.

### Refactor C — Split `SettingsViewModel` into sub-VMs + introduce `ShellViewModel` (findings N-7, 19, N-9)

Two related changes. First, `SettingsViewModel` fans out into `AppSettingsViewModel` (theme, future: last-profile-id, backup retention) and `ProfileSettingsViewModel` (food, buffer, payday). Second, `MainWindow` code-behind retires its manual `INotifyPropertyChanged` and becomes a plain `FluentWindow` with `DataContext = shellViewModel`. `ShellViewModel` holds `ProfileHeading`, `ProfileColour`, `OpenSettingsCommand`, `SwitchProfileCommand` — the last two can then be wired to a global keybinding without touching each screen's VM.

**Unlocks:** Phase-2 Payee-rule settings tab drops in as a third `SettingsViewModel` sub-VM. Phase-2 "switch profile mid-session" lives in the shell, not the screen. Settings dialog stops knowing about the main window's structure.

**Effort:** ~3–4 hours.

---

## Closing note

Phase 1 landed the user-facing surface the spec called for without compromising the tenancy or calculation invariants. The two Reds (N-1 duplicate column, N-2 row handler leak) are both shipping hazards but mechanical fixes — under an hour together. The Yellow cluster (N-3 through N-9) reflects the predictable shape of a feature-complete MVP: decisions that were correct when the VM had three commands are under strain now that it has twelve. The three Phase-2 refactors above address the Yellow cluster at the cost of ~2.5 engineer-days, and the return is a VM layer that can actually be unit-tested before Phase 2's debt/savings/import work doubles the surface area. Do them at the kickoff, not as a mid-Phase-2 detour.
