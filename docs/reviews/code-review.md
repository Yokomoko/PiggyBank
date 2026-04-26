# PiggyBank — Week 2 Code Review

> Review date: 2026-04-22 · Reviewer: Claude (fresh eyes pass)
> Scope: full `src/` + `tests/` tree at commit state on 2026-04-22
> Context: `C:\Users\achapman\PiggyBankWPF\03-architecture.md`, `00-migration-blueprint.md`

---

## 1. Verdict

**Solid skeleton, honest gaps.** The tenancy rails, calculator, and payday math are genuinely strong and test-covered; the UI is a Week-2 shopfront with several unfinished wires that will bite the moment a second feature gets pinned to them.

---

## 2. Top 5 findings

| # | Severity | Finding |
|---|---|---|
| 1 | Red | `MainWindow.ProfileColour` is bound in XAML but never set from `App.xaml.cs`; `ProfilePickerWindow.xaml` binds a `string` hex to `Border.Background` without a converter — the profile-colour ring will be invisible / broken at runtime. (§4, findings 1 + 2) |
| 2 | Red | `App.xaml.cs` has no global dispatcher exception handler and `OnStartup` is `async void` — any exception during host start-up or profile boot crashes the process silently. Architecture §10 says this is mandatory. (§4, finding 3) |
| 3 | Red | `CurrentMonthView` DataGrid has `CanUserAddRows="True"` on the ledger, but there is no `Adding`/`CommittingEdit` handler and `Transactions` is a `TransactionRow` collection, not the domain entity — new rows never persist. (§4, finding 4) |
| 4 | Yellow | `CurrentMonthViewModel.IsWageVisible` / `ToggleWageCommand` / the "Show wage" button are a full-width half-feature: the flag is toggled but nothing in the XAML or row projections redacts the amount. Either finish or delete. (§4, finding 5) |
| 5 | Yellow | `CurrentMonthViewModel.CreateCurrentMonthAsync` resolves `AppDbContext` directly from DI and queries `ProfileSettings` with EF — the App layer is reaching around the repository pattern the architecture doc set out. (§4, finding 6) |

---

## 3. Full findings list

### Finding 1 — Red: ProfileColour binding dead-ends

**File:** `src/PiggyBank.App/MainWindow.xaml:29`, `src/PiggyBank.App/MainWindow.xaml.cs:20-24`, `src/PiggyBank.App/App.xaml.cs:43-47`

`MainWindow.xaml` binds the title-bar ring `Background="{Binding ProfileColour}"`, and `MainWindow.xaml.cs` exposes `ProfileColour` as a `Brush` with manual `INotifyPropertyChanged` plumbing. **Nothing ever sets this property.** `App.OnStartup` sets `mainWindow.ProfileHeading` but not `ProfileColour`, so the ring renders with `Brushes.Transparent`.

**Fix:**

```csharp
// App.xaml.cs, after resolving `profile`:
mainWindow.ProfileHeading = profile is null
    ? "Welcome."
    : $"Signed in as {profile.DisplayName}.";
mainWindow.ProfileColour = profile is null
    ? Brushes.Transparent
    : (Brush)new BrushConverter().ConvertFromString(profile.ColourHex)!;
```

Longer-term: replace `MainWindow`'s ad-hoc `INotifyPropertyChanged` with a proper `ShellViewModel` (see finding 19).

---

### Finding 2 — Red: Profile picker list binds hex string as Brush

**File:** `src/PiggyBank.App/Views/ProfilePickerWindow.xaml:35`

```xml
<Border Width="14" Height="14" CornerRadius="7"
        Background="{Binding ColourHex}"
        VerticalAlignment="Center" />
```

`ColourHex` on `Profile` is a `string` like `"#3B82F6"`. WPF does not auto-parse hex strings into `Brush` on arbitrary bindings (it only works via the XAML parser at compile time for literal values). This will quietly throw a binding error at runtime and render nothing.

**Fix:** add a `StringToBrushConverter` (or use the same conversion logic in a `ProfileViewModel` exposing a `Brush`). Example:

```csharp
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? v, Type _, object? __, CultureInfo ___)
        => v is string hex
            ? (Brush)(new BrushConverter().ConvertFromString(hex) ?? Brushes.Transparent)
            : Brushes.Transparent;
    public object ConvertBack(object? v, Type _, object? __, CultureInfo ___)
        => throw new NotSupportedException();
}
```

---

### Finding 3 — Red: No global exception handler; `async void OnStartup`

**File:** `src/PiggyBank.App/App.xaml.cs:18, 99`

Architecture doc §10 specifies `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. None are wired. Combined with `async void OnStartup`, any failure in `_host.StartAsync()`, `EnsureInitialisedAsync`, `MigrateAsync`, or `TouchLastOpenedAsync` crashes the app without a user-facing message, and the exception disappears from any shell too (no Serilog File sink is wired despite the NuGet being referenced).

**Fix:**

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    DispatcherUnhandledException += OnUnhandledDispatcher;
    AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomain;
    TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    try
    {
        _host = AppHost.Build();
        await _host.StartAsync();
        // ... rest of boot
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Startup failed: {ex.Message}", "Money Manager",
            MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
```

Also: the `Serilog.Sinks.File` package is referenced in `PiggyBank.App.csproj` but never configured (`AppHost.Build()` only wires `AddDebug`). Either wire Serilog or drop the package.

---

### Finding 4 — Red: Ledger DataGrid add-row persists nothing

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthView.xaml:126`

```xml
<DataGrid ... ItemsSource="{Binding Transactions}"
          CanUserAddRows="True"
          ... >
```

`Transactions` is `ObservableCollection<TransactionRow>`. `TransactionRow` is a local view-only projection — it has no back-reference to `AppDbContext`, no command wiring, no `RowEditEnding`/`AddingNewItem` handler. When a user hits `Enter`, WPF will try to `Activator.CreateInstance<TransactionRow>()` which will throw (the row has a required constructor parameter of type `Transaction`), or at best creates an unpersisted row that vanishes on next `LoadAsync`.

**Fix (pick one):**

- Short-term, honest: `CanUserAddRows="False"` and add a dedicated "Quick add spend" button as the architecture doc already envisages.
- Proper: introduce `ITransactionRepository.AddAsync` behind a `QuickAddSpendCommand` (taking payee/amount/category), keep the grid read-only.

---

### Finding 5 — Yellow: `IsWageVisible` is a half-feature

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:33, 145`, `CurrentMonthView.xaml:29-32`

`IsWageVisible` is toggled by `ToggleWageCommand` but:

1. The button label is the static string `"Show wage"` — doesn't change to `"Hide wage"`.
2. `MonthlyOutgoingRow.IsWage` is set but the XAML `DataGridTextColumn Binding="{Binding Amount, ...}"` doesn't consult `IsWage` or `IsWageVisible`.
3. The spec (§9) says `MonthlyOutgoing` rows with `IsWage=true` should display `"****"` when `IsWageVisible=false` — the current UI never redacts.
4. There is a `ProfileSettings.WageVisible` column in the DB, never read or written.

**Fix:** Either delete the property + command now and re-add in a later week, or implement:

```csharp
// MonthlyOutgoingRow
public string AmountDisplay(bool isWageVisible) =>
    IsWage && !isWageVisible ? "****" : Amount.ToString("£#,##0.00;-£#,##0.00");
```

with `MultiBinding` on the column, or a simple `IValueConverter` keyed off `IsWage` and the VM's `IsWageVisible`.

---

### Finding 6 — Yellow: VM reaches around repositories into `AppDbContext`

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:129-132`

```csharp
var db = scope.GetRequiredService<AppDbContext>();
var settings = await db.ProfileSettings.FirstOrDefaultAsync()
    ?? new ProfileSettings();
```

Two issues:

1. The VM directly depends on `AppDbContext` and `Microsoft.EntityFrameworkCore`. Architecture §4 and the portable-core rule say the App layer must go through repositories/services. This is the first bleed; if it stays it multiplies.
2. The `?? new ProfileSettings()` fallback swallows the data-consistency invariant that every profile gets a `ProfileSettings` row inserted by `ProfileAdminService.CreateAsync`. If it's missing, that's a bug we want loud, not silent.

**Fix:**

```csharp
public interface IProfileSettingsRepository
{
    Task<ProfileSettings> GetAsync(CancellationToken ct = default);
}
// …
var settings = await scope.GetRequiredService<IProfileSettingsRepository>().GetAsync(ct);
```

The repo method throws `InvalidOperationException` if the row is missing — a loud failure beats a silent default.

---

### Finding 7 — Yellow: Service-locator pattern pervasive in VMs

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:63-67, 101, 115, 127-128`

Every interaction between `CurrentMonthViewModel` and data goes through `_sessions.Current.Services.GetRequiredService<T>()`. Architecture §4: "Never use `ServiceLocator` anti-pattern inside ViewModels."

This is structurally awkward because repositories/services are scoped per-profile and the VM is Transient on root DI. The clean fix is to inject a **scoped factory** instead of DI resolution at call site:

```csharp
public interface ICurrentScopeAccessor
{
    T Get<T>() where T : notnull;   // under the hood, _sessions.Current.Services
}
```

This keeps the VM testable (mock `ICurrentScopeAccessor`), still honours the scoped repository lifetime, and removes the "dig through containers" feel. Lower priority than findings 1–6, but worth a half-day refactor before more VMs copy-paste the same pattern.

---

### Finding 8 — Yellow: `BufferPerDay` hardcoded in VM

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:161`

```csharp
var inputs = new BudgetInputs(GrandTotal, MonthlySpendTotal, DaysSincePayday, DaysToNextPayday, BufferPerDay: 10m);
```

`ProfileSettings.BufferPerDay` already exists (defaulted to `10m`) and was designed to drive this exact input. The VM ignores the DB value so the settings UI (when it lands) will not take effect on the allowance engine.

**Fix:** read `ProfileSettings` once on load, store it on the VM, pass `settings.BufferPerDay` into `BudgetInputs`.

---

### Finding 9 — Yellow: `MonthlyOutgoing` loses `IsIncome` flag on snapshot

**File:** `src/PiggyBank.Core/Entities/MonthlyOutgoing.cs:9-22`, `src/PiggyBank.Data/Services/MonthService.cs:59-68`

`RecurringOutgoing` has `IsIncome`; `MonthlyOutgoing` does not. During rollover, the snapshot drops it. For the current allowance calc the amount sign conveys income vs bill, but this has two consequences worth flagging now:

1. Analytics / category filters (Phase 2, §2 of roadmap) cannot say "show me income lines" from closed months without re-joining to the template or infering from sign.
2. The domain doc (§6) says `MonthlyOutgoing` is a point-in-time snapshot — if a user later flips a `RecurringOutgoing.IsIncome` in the template, the historic months lose faithful representation.

**Fix:** mirror `IsIncome` on `MonthlyOutgoing` and copy it in `MonthService.CreateAsync`. One migration, three lines of code.

---

### Finding 10 — Yellow: `ProfileAdminService.CreateAsync` error path is leaky

**File:** `src/PiggyBank.Data/Profiles/ProfileAdminService.cs:42-94`

The method correctly begins a transaction, but:

- `SaveChangesAsync` is called **twice** (line 63 for the Profile, then line 91 for Categories + Settings). The first save commits Profile into the transaction, but if the second save throws the transaction will be rolled back — which is correct. However, there is no `try`/`catch` and the transaction isn't explicitly rolled back; relying on the `await using` means the scope-disposal path must do `RollbackAsync`. EF Core does this correctly, but I'd prefer an explicit try-catch to attach a diagnostic log ("Profile creation failed at step: seed-category-copy").
- If any category insert throws mid-loop (bad data, interceptor throws), the profile-already-saved state only exists inside the open transaction, so rollback is safe — but the code reads as if the author thinks "Profile is committed". A comment or an explicit single `SaveChanges` at the end would clarify intent.
- Input validation: `colourHex` is trusted blindly. The DB schema limits it to 9 characters — a caller passing `"not a colour"` writes junk. Also, the display name trim happens but length-bound (80 chars on the column) is not validated.

**Fix:**

```csharp
public async Task<Profile> CreateAsync(
    string displayName, string colourHex, string iconKey,
    IEnumerable<int>? seedCategoryIds = null,
    ProfileSettingsInput? settings = null,
    CancellationToken ct = default)
{
    EnsureAdminScope();

    if (string.IsNullOrWhiteSpace(displayName))
        throw new ArgumentException("DisplayName required.", nameof(displayName));
    if (displayName.Length > 80)
        throw new ArgumentException("DisplayName exceeds 80 characters.", nameof(displayName));
    if (!HexColourRegex.IsMatch(colourHex))
        throw new ArgumentException($"'{colourHex}' is not a valid hex colour.", nameof(colourHex));

    var profile = new Profile { DisplayName = displayName.Trim(), ColourHex = colourHex, IconKey = iconKey };

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
        db.Profiles.Add(profile);

        var selectedIds = (seedCategoryIds ?? []).ToHashSet();
        var seedsToCopy = selectedIds.Count > 0
            ? await db.SeedCategories.Where(s => selectedIds.Contains(s.Id)).ToListAsync(ct)
            : await db.SeedCategories.Where(s => s.DefaultEnabled).ToListAsync(ct);

        foreach (var seed in seedsToCopy)
            db.Categories.Add(new Category { ProfileId = profile.Id, Name = seed.Name,
                                             Kind = seed.Kind, SourceSeedCategoryId = seed.Id });

        var profileSettings = new ProfileSettings { ProfileId = profile.Id };
        if (settings is not null)
        {
            profileSettings.PrimaryPaydayDayOfMonth = settings.PrimaryPaydayDayOfMonth;
            profileSettings.AdjustPaydayForWeekendsAndBankHolidays = settings.AdjustPaydayForWeekendsAndBankHolidays;
        }
        db.ProfileSettings.Add(profileSettings);

        await db.SaveChangesAsync(ct);  // ONE save, then commit
        await tx.CommitAsync(ct);
        return profile;
    }
    catch
    {
        await tx.RollbackAsync(ct);
        throw;
    }
}
```

One save is cleaner, and the `try` frame gives you a named seam to attach logging to.

---

### Finding 11 — Yellow: `async void` event handlers chain into the UI load path

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthView.xaml.cs:15-19`, `src/PiggyBank.App/Views/CreateProfileWindow.xaml.cs:22`, `src/PiggyBank.App/Views/ProfilePickerWindow.xaml.cs:31`

```csharp
Loaded += async (_, _) =>
{
    if (DataContext is CurrentMonthViewModel current)
        await current.LoadCommand.ExecuteAsync(null);
};
```

`async void` on `Loaded` is permitted (event handler), but:

- If `LoadAsync` throws (it awaits DB work), the exception propagates to the dispatcher. With no global handler (finding 3) the app dies.
- There is no cancellation — if the user navigates away mid-load, the load continues.
- The `LoadAsync` method already has `[RelayCommand]` generated for it but the class also re-declares `public async Task LoadAsync(...)` — harmless, but the public `LoadAsync` is now resolved at the call site instead of the generated command. Pick one idiom.

**Fix:** wrap in try/catch with logging, and prefer the generated command consistently.

---

### Finding 12 — Yellow: Hardcoded strings everywhere in XAML

**File:** multiple XAML files (`CurrentMonthView.xaml:23, 96, 142, 184`, `CreateProfileWindow.xaml:20-23, 30-37`, etc.)

Every user-facing string is a literal (`"Current Month"`, `"Outgoings"`, `"ALLOWED TODAY"`, `"Choose profile — PiggyBank"`, …). For a single-user two-person app this is fine for MVP — but if i18n ever matters, or even just centralised copy-editing, move them to a shared resource dictionary. Flag only, no action required at Week 3.

---

### Finding 13 — Yellow: Currency / date format strings duplicated

**File:** `CurrentMonthView.xaml:106, 131, 146, 162, 165, 168, 171, 174, 177, 195`

`StringFormat=£#\,##0.00` appears 8 times; `StringFormat=dd MMM` appears once. Extract to resource keys:

```xml
<sys:String x:Key="FmtGbp">£#,##0.00</sys:String>
<sys:String x:Key="FmtGbpSignedLong">£+#,##0.00;-£#,##0.00;£0</sys:String>
```

Then use `StringFormat="{StaticResource FmtGbp}"`. One-string change when the designer wants pennies dropped or an inline `+` added.

---

### Finding 14 — Green: `TestDb.Tenant.GetType()` is a no-op "diagnostic"

**File:** `tests/PiggyBank.Data.Tests/Tenancy/TestDbFactory.cs:62`

```csharp
Tenant.GetType(); // retained only for diagnostics; this TestDb's Tenant is the admin one
```

This does literally nothing. It's not a breakpoint anchor in compiled code and doesn't prove anything to a reader. Delete the line and the stale comment.

---

### Finding 15 — Green: Dead migration `SeedCategoryDefaultEnabled`

**File:** `src/PiggyBank.Data/Migrations/20260422204815_SeedCategoryDefaultEnabled.cs`

`Up` and `Down` are both empty. The column was already added in `InitialCreate`. Either delete the migration (and its `Designer.cs`), or keep it but mark with a comment explaining it was a no-op checkpoint. Personally: delete — migration noise compounds.

---

### Finding 16 — Green: Naming inconsistency on `MonthlyOutgoing`

**File:** `src/PiggyBank.Core/Entities/MonthlyOutgoing.cs` vs `src/PiggyBank.Core/Entities/RecurringOutgoing.cs`

`RecurringOutgoing` has `Notes`, `IsIncome`, `IsArchived`; `MonthlyOutgoing` has only `IsWage` and `SortOrder`. The asymmetry is partially deliberate (snapshot is sparser), but `Notes` is an obvious omission (the user wants to annotate why Mortgage dropped £20 in March 2024 for an overpayment) and `IsIncome` was discussed in finding 9.

---

### Finding 17 — Green: `BudgetCalculator._clock` is unused

**File:** `src/PiggyBank.Core/Budgeting/BudgetCalculator.cs:13-17`

```csharp
public sealed class BudgetCalculator(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock;
```

No method uses `_clock`. Either remove the parameter (tests build it with `TimeProvider.System`, so you get to simplify the test constructor) or add a comment documenting the planned use. The field has a comment saying "retained for future use" — fine as a deferral, but at Week 3 decide whether any calc actually needs it and remove if not.

---

### Finding 18 — Green: `_month` on `CurrentMonthViewModel` holds the entity, not a DTO

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:30, 151, 158-159`

Keeping a live EF entity on a Transient ViewModel is OK for reads but risks: the VM does `Month.NextPayday.DayNumber` to compute `DaysToNextPayday`, which is fine, but `Month.CarriedOverBalance` is also read on every `Recalculate()` — if the entity ever gets detached or the scope ends while the VM lingers, `LazyLoading` (not enabled) or captured state becomes a hazard. For a Transient VM this is probably safe, but worth a comment flagging it.

Lower urgency than the half-features above.

---

### Finding 19 — Yellow: `MainWindow.xaml.cs` rolls its own `INotifyPropertyChanged`

**File:** `src/PiggyBank.App/MainWindow.xaml.cs:9-26`

```csharp
public partial class MainWindow : FluentWindow, INotifyPropertyChanged
{
    private string _profileHeading = "";
    private Brush _profileColour = Brushes.Transparent;

    public string ProfileHeading
    {
        get => _profileHeading;
        set { _profileHeading = value; PropertyChanged?.Invoke(this, new(nameof(ProfileHeading))); }
    }
    // ...
    public event PropertyChangedEventHandler? PropertyChanged;
```

The whole codebase uses `ObservableObject` + `[ObservableProperty]`. Don't start a second convention in the shell. Refactor to `ShellViewModel : ObservableObject` with `[ObservableProperty] private string _profileHeading;` and `[ObservableProperty] private Brush _profileColour;`. Bonus: the DataContext becomes testable and `App.xaml.cs` stops poking at window internals.

---

### Finding 20 — Green: `Profile.ColourHex` not validated, not a type

**File:** `src/PiggyBank.Core/Entities/Profile.cs:12`

`ColourHex` is a `string`. EF Core limits the column to 9 chars. No central validation. Consider a value-object wrapper or at minimum a constant regex. If the user types "red" into the (future) colour picker it'll persist.

---

### Finding 21 — Green: `ProfileAdminService.ListAsync` order undefined for fresh profiles

**File:** `src/PiggyBank.Data/Profiles/ProfileAdminService.cs:27-28`

```csharp
.OrderByDescending(p => p.LastOpenedAtUtc)
.ThenBy(p => p.DisplayName)
```

SQLite orders NULLs last when descending (which is what we want for "never opened" → appear after recently-opened ones). Worth an explicit unit test so the behaviour survives a dialect swap.

---

### Finding 22 — Green: `CategoryRepository.AddAsync` signature mixes concerns

**File:** `src/PiggyBank.Data/Repositories/CategoryRepository.cs:18-25`

Accepts `name, kind, colourHex?` but its sibling repos take the whole entity. Pick one style. For a two-person app, "pass the entity" is the simpler convention — the VM can be responsible for `.Trim()` and a default colour. That also removes the string-mutation and the ad-hoc `if (!string.IsNullOrWhiteSpace(...))` switch.

---

### Finding 23 — Green: `TransactionRepository.SumByCategoryForMonthAsync` materialises all Category rows

**File:** `src/PiggyBank.Data/Repositories/TransactionRepository.cs:34-35`

```csharp
var categoryNames = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
```

Loads every non-archived category, regardless of which ones the current month actually has transactions for. For MVP scale (≤40 categories) this is fine. At Phase 3 scale (potentially more) consider:

```csharp
var relevantIds = results.Where(r => r.CategoryId.HasValue).Select(r => r.CategoryId!.Value).ToList();
var categoryNames = await db.Categories
    .Where(c => relevantIds.Contains(c.Id))
    .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
```

Flag only, no change required.

---

### Finding 24 — Green: `RepositoryTenantLeakTests.months.Single()` will fail confusingly

**File:** `tests/PiggyBank.Data.Tests/Tenancy/RepositoryTenantLeakTests.cs:46`

If a future dev adds a second month per profile in `SeedProfileAsync`, `months.Single()` throws `InvalidOperationException` — the test fails as if the tenancy broke, not as if the seed changed. Prefer `months.Should().HaveCount(1); var bMonth = months[0];`.

---

### Finding 25 — Green: `CreateCurrentMonthAsync` misses `CancellationToken`

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:124`

```csharp
public async Task CreateCurrentMonthAsync()
```

Every other public async method in the VM takes a `CancellationToken ct = default`. This one doesn't, nor does it forward to the repo call:

```csharp
await svc.CreateAsync(lastPayday, nextPayday);
await LoadAsync();
```

Should be:

```csharp
public async Task CreateCurrentMonthAsync(CancellationToken ct = default)
{
    // ...
    await svc.CreateAsync(lastPayday, nextPayday, ct: ct);
    await LoadAsync(ct);
}
```

Minor, but this is how inconsistency spreads.

---

### Finding 26 — Green: `CreateProfileViewModel.ColourHex` is set-only from code

**File:** `src/PiggyBank.App/Profiles/CreateProfileViewModel.cs:19-23`, `src/PiggyBank.App/Views/CreateProfileWindow.xaml`

`ColourHex` and `IconKey` are observable properties, but the XAML **has no picker** for either. They're born at the default and die at the default. The architecture roadmap (Decision 4 in `00-migration-blueprint.md`) says profiles get "subtle neutral accents" — acceptable to postpone the picker, but then the VM property is ceremony without value. Trim or finish.

---

### Finding 27 — Green: Filepath conventions match, layout sensible

**File:** whole tree

File naming matches type naming, folders line up with namespaces, the one outlier is `DesignTimeDbContextFactory` (lives at project root — fine, but most projects put it in a `Design/` folder; bikeshed level). The empty `PiggyBank.Import` project with no `.cs` files and a correspondingly empty `PiggyBank.Import.Tests` is intentional scaffolding per the roadmap (Phase 1 Week 3), not a smell.

---

## 4. Dead code inventory

Remove or justify each:

- **`Profile.IconKey`** (`Profile.cs:13`) — set by the create wizard, stored in DB, never displayed. If you're not going to render an icon in Week 3, delete the property and the column (or at least the wizard wiring). If you are, add it to the picker + title-bar template.
- **`Profile.PinHash` / `Profile.PinSalt`** (`Profile.cs:14-15`) — Phase 3 fields. Fine to keep in DDL but add `[NotMapped]` or leave a comment explaining they pre-date their feature.
- **`AppSettings` entity** (`AppSettings.cs` + `AppDbContext.cs:28, 58-63` + migration) — zero reads, zero writes anywhere. Either plug it into actual settings (theme switcher, last profile id) or delete.
- **`AppSettings.LastProfileId` / `LastProfileOpenedAtUtc`** — ditto; roadmap 2-second auto-select depends on them.
- **`CurrentMonthViewModel.IsWageVisible` + `ToggleWageCommand`** — half-feature (finding 5).
- **`CurrentMonthViewModel.AvailableCategories`** (line 28, populated line 82-83, never bound in XAML) — pre-wired for the category dropdown in a future quick-add flow. Delete or add the dropdown.
- **`CurrentMonthViewModel.DaysSincePayday` / `DaysToNextPayday` / `Total` / `MonthlySpendTotal`** — observable properties never rendered (only `GrandTotal`, `AllowedSpendPerDay` etc. are). They exist so `Recalculate` can compose — consider making them plain fields instead of `[ObservableProperty]`.
- **`BudgetCalculator._clock`** (finding 17).
- **`TestDbFactory` line 62** `Tenant.GetType();` (finding 14).
- **`20260422204815_SeedCategoryDefaultEnabled` migration** (finding 15).
- **`MainWindow.ProfileColour`** (finding 1) — dead wire, should either be fixed or deleted.
- **NuGet `Serilog.Sinks.File`** in `PiggyBank.App.csproj:13` — not referenced anywhere in code.
- **NuGet `LiveChartsCore.SkiaSharpView.WPF`** in `PiggyBank.App.csproj:11` — no charts yet; fine to pre-pull but note the cost in binary size.
- **`CreateProfileViewModel.ColourHex` / `IconKey`** — no UI (finding 26).

---

## 5. Incomplete features (looks done but isn't)

1. **Profile colour badge in title bar** — XAML bound, VM absent. (findings 1, 19)
2. **Profile colour swatch in picker list** — binding broken. (finding 2)
3. **Wage privacy toggle** — flag flips, UI never redacts. (finding 5)
4. **Ledger inline add-row** — DataGrid allows it, nothing persists. (finding 4)
5. **Category picker for transactions** — `AvailableCategories` populated but never bound.
6. **Settings UI for `BufferPerDay` / `DailyFoodBudget`** — fields exist, no screen.
7. **Serilog file sink** — package referenced, handler not wired.
8. **Global exception handler** — spec requires, code omits.
9. **Profile create — colour/icon picker** — VM props exist, XAML absent.
10. **`AppSettings` singleton** — entity scaffolded, never loaded or saved.

---

## 6. Five patterns done well — keep doing these

1. **Tenancy safety net is robust.** `ITenantContext` + `MutableTenantContext.Set(...)` being locked-once, the interceptor blocking `ProfileId` mutation, the global query filter applied via `ModelBuilder` reflection, **and** a reflection-driven `TenantLeakTests` that auto-covers new `ProfileOwnedEntity` types — these four layers reinforce each other and the tests prove it. Don't weaken any of them.
2. **Pure `BudgetCalculator` with record inputs.** `BudgetInputs` is a clean DTO, the calculator methods are simple expressions, every one is unit-tested with boundary cases (zero days, overspent, sign flips, buffer-at-zero). Exactly as §7 of the architecture doc calls for.
3. **Snapshot vs template separation.** `RecurringOutgoing → MonthlyOutgoing` snapshot on month creation is correctly implemented in `MonthService.CreateAsync` and verified by `MonthServiceTests`. Archived templates are excluded. History stays honest.
4. **Manual rollover flow with `SuggestRolloverAsync` / `ApplyRolloverFromPriorAsync`.** The "no silent cascade" rule lives in code, not just in docs — the service throws on open-prior, returns null on no-prior, and the VM exposes the banner only when an override hasn't been applied. Good UX hygiene baked into the domain.
5. **Payday calculator with bank-holiday table.** `PaydayCalculator.ResolveForMonth` is side-effect-free, parameterised, tested across weekend + bank-holiday + clamp-to-end-of-month edges. `UkBankHolidays` is hand-rolled to 2030 — deliberately offline, deliberately deterministic. Exactly the right energy for a personal-finance app.

---

## 7. Refactor wishlist for Week 3

1. **Introduce a `ShellViewModel` and retire `MainWindow.xaml.cs`'s manual `INotifyPropertyChanged`.** Same move lets you solve findings 1 and 19 in one go, and gives you a clean parking spot for global commands (switch profile, toggle wage, view settings) that will otherwise end up pasted into every screen's VM. ~1 hour.

2. **Extract `OnModelCreating` configurations into `IEntityTypeConfiguration<T>` per entity.** `AppDbContext.OnModelCreating` is 109 lines of per-entity fluent config. Moving each entity block into e.g. `Data/Configurations/CategoryConfiguration.cs` brings the context down to ~30 lines and makes code-review diffs scoped when you add Phase 2 entities (Debt, DebtSnapshot, SavingsProjection, PayeeRule). Use `b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` as the architecture doc already hints at. ~2 hours.

3. **Carve the "App reaches into EF Core" bleed out now.** Findings 6, 7, 8 all stem from the VM being too friendly with `AppDbContext`. Introduce:
   - `IProfileSettingsRepository` (read-only singleton row).
   - `ICurrentScopeAccessor` (replaces `_sessions.Current.Services.GetRequiredService<T>()`).

   A half-day now saves a full-day retrofit once a second VM (`DashboardViewModel`, `SettingsViewModel`) copies the pattern. While you're in there, consider whether `MonthService` deserves an `IMonthService` interface — currently it's the only non-interfaced service in the Data layer, which breaks a small pattern.

---

## Closing note

For a Week-2 output this is healthier than most "MVP" code: the invariants that matter (multi-tenancy isolation, allowance math, payday math) are backed by tests that would fail loud if broken. The rough edges cluster in the UI wire-up — which is exactly where a Week-3 features sprint will either clean them up or fossilise them. Tighten findings 1–6 before any new screen lands.
