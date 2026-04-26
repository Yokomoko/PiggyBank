# Architecture Review — End of Phase 1 MVP

Reviewer: Claude (senior architecture reviewer)
Date: 2026-04-22
Scope: `C:\ScmWorkspaces\PiggyBank` — 8 projects, commits on `main` since `e63c6d6`
Benchmarks: `C:\Users\achapman\PiggyBankWPF\03-architecture.md` (§13 tenancy addendum), `PiggyBank-WPF-Spec.md` §2.0 portable-core, prior review at `docs\reviews\arch-review.md`

---

## 1. Verdict

**Prior review actioned in full. Ship Phase 1.** All 8 must-fix / yellow items from the Week 2 review have been genuinely closed (not papered over). Multi-tenancy safety rails 1–7 are all green now — `AddTenantForeignKeys` migration (`20260422212515`) actually adds the four missing FKs, and `TenantLeakTests` is genuinely reflection-driven with the no-vacuous-pass guard I asked for. **Two new 🔴 items emerged from Week 3 work** (ledger `IsFuture` column duplicated in XAML, and `OutgoingRowChanged` / `TransactionRowChanged` use `async void` event handlers with no exception routing), plus a short list of 🟡 smells around the god-VM and persistent service-locator. Neither blocker prevents Phase 1 sign-off — but fix them in the first 30 minutes of Phase 2 before the import wizard adds another 10 properties to this VM.

---

## 2. Status of prior findings

### Genuinely closed (7)

| # | Finding | Evidence of genuine fix |
|---|---|---|
| 3.1 | Missing FKs on 4 tenant tables | `Migrations/20260422212515_AddTenantForeignKeys.cs:13-43` adds all four; `AppDbContext.cs:86-148` shows `HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(Cascade)` on every tenant entity. `MonthlyOutgoing` and `Transaction` also gained `HasOne<Month>().WithMany().OnDelete(Cascade)` as a bonus — good, covers the Phase-3 hard-delete path. |
| 3.2 | `DateTime.UtcNow` in Core | `Profile.cs:19` now `public DateTime CreatedAtUtc { get; set; }` with no default. Doc comment on `:16-18` explains the rule. `ProfileAdminService.cs:18` takes `TimeProvider clock`, `:20 NowUtc()` helper is used at `:61, 107, 120` for `CreatedAtUtc`, `ArchivedAtUtc`, `LastOpenedAtUtc`. Core grep for `DateTime.UtcNow/Now` returns only doc comments. Clean. |
| 3.3 | `RepositoryTenantLeakTests` hand-rolled | `TenantLeakTests.cs:78-86` reflects over `typeof(AppDbContext).GetProperties()` and filters to `DbSet<T> where T : ProfileOwnedEntity`; `:94-131` seeds every discovered type in both profiles then `:69-74` asserts `NotEmpty` (no vacuous pass) paired with `OnlyContain`. Exactly the "belt + braces" I asked for. |
| 3.6 | Rollover double-fire risk (in the old review that's Risk B, but was implicitly shared with 3.6) | Close-month ceremony now explicit (`CurrentMonthViewModel.cs:156-163` `CloseMonthAsync`, banner at `CurrentMonthView.xaml:332-340`). `SuggestedRollover` is surfaced only when prior is closed (`MonthService.cs:92-93`). |
| – | MainWindow.ProfileColour hex-to-Brush | `HexToBrushConverter.cs:13-34` added; wired in `App.xaml:15` as static resource `HexToBrush`, consumed by `MainWindow.xaml:29` and `ProfilePickerWindow.xaml:35`. MainWindow no longer holds a `SolidColorBrush` type. |
| – | Global exception handler | `App.xaml.cs:28-30` installs all three (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`); `:157-179` routes through logger + crash file at `%LocalAppData%\PiggyBank\Logs\crash-*.txt` + message box. Handler installed before `BootAsync` so a startup throw gets captured. |
| – | `CanUserAddRows="False"` with no alternative | Quick-add forms present for both grids (`CurrentMonthView.xaml:140-158` outgoings, `:224-252` spend). `Ctrl+N` focuses the payee box (`CurrentMonthView.xaml.cs:25-27`). Keyboard-first entry actually reachable now. |

### Partially addressed / regressed (1)

| # | Finding | Status |
|---|---|---|
| 3.5 | Service-locator in `CurrentMonthViewModel` | **Worse.** At the prior review, 4 `GetRequiredService<T>` calls in `LoadAsync`. Now 18+ across the VM (`CurrentMonthViewModel.cs:83-87, 123, 138-139, 159, 173, 193, 208, 240, 251, 280, 309, 329, 344`). Every new command repeated the `_sessions.Current!.Services.GetRequiredService<T>()` incantation. Not a Phase-1 blocker but the pattern is now baked in — the longer it lives, the more painful the fix (see §4). |

### Still open (non-regressed — same severity as before)

| # | Finding | Status |
|---|---|---|
| 3.4 | `MonthRepository.ListAsync()` no pagination | Unchanged — still 🟡, fine for Phase 1. |
| 3.6 | `ProfileSessionManager.OpenProfile` not thread-safe | Unchanged — `OpenProfile` still only called from `App.BootAsync`; once you add "Switch profile" in Phase 2 this becomes reachable. |
| 3.7 | `ProfileSession` dual-constructor | Unchanged — still confusing but works. `AdminScope` static factory (`ProfileSession.cs:26-32`) calls the private ctor; `IsAdminScope` is still not exposed on `ProfileSession` (only on `ITenantContext`). |
| 3.8 | Dead line in `TestDb.cs` | No longer present — `TestDb` rewritten, the `Tenant.GetType()` diagnostic is gone. |
| 3.9 | Empty `SeedCategoryDefaultEnabled` migration | **Still there.** `20260422204815_SeedCategoryDefaultEnabled.cs:10-20` both `Up()` and `Down()` are empty braces. Harmless but confusing archaeology. Delete or comment. |
| 3.10 | `AppSettings.Id = 1` + `Autoincrement` | **Still there.** Model snapshot confirms `ValueGeneratedOnAdd` + `INTEGER` (`AppDbContextModelSnapshot.cs:20-24`). Low risk, but spec §6 says "singleton row" — `.ValueGeneratedNever()` would make that explicit. |
| 3.11 | VM property count | **Worse — see §3 new findings.** |

---

## 3. New findings introduced in Phase 1 work

### 3.1 🔴 Duplicate `IsFuture` column in ledger DataGrid

**File:** `CurrentMonthView.xaml:206-211`

```xml
<DataGridCheckBoxColumn Header="Future"
                        Binding="{Binding IsFuture, Mode=TwoWay}"
                        Width="60" />
<DataGridCheckBoxColumn Header="Future"
                        Binding="{Binding IsFuture, Mode=TwoWay}"
                        Width="60" />
```

Two identical columns side-by-side. Both bind to the same `IsFuture` property; either works, but the user sees two "Future" checkboxes per row. This is a straight copy-paste bug, probably from iterative XAML editing. 5-second fix — delete lines 206-208 (or 209-211). Flagged 🔴 because it ships to the user and looks wrong.

### 3.2 🔴 `async void` row-change handlers with no exception routing

**File:** `CurrentMonthViewModel.cs:301-340`

```csharp
private async void OutgoingRowChanged(object? sender, PropertyChangedEventArgs e) { ... }
private async void TransactionRowChanged(object? sender, PropertyChangedEventArgs e) { ... }
```

Both are `async void`. They call `repo.UpdateAsync(entity)` which throws on DB failure (constraint violation, stamp-interceptor reject, etc.). An exception raised inside an `async void` on a non-UI `SynchronizationContext` thread will reach `AppDomain.UnhandledException` and, depending on WPF's handler-priority order, *may or may not* be swallowed by `TaskScheduler.UnobservedTaskException`. The global crash-file handler in `App.xaml.cs:145-149` catches it eventually, but the user sees a crash dialog for a failed edit of a row — not a graceful "couldn't save that, try again" toast.

This is architecture §10 Risk 2 — WPF memory/lifetime hazards of event subscriptions to long-lived services. The code currently subscribes `row.PropertyChanged += OutgoingRowChanged` at `WireOutgoing` (`:291`) but **never unsubscribes** when the row is removed (e.g. `DeleteOutgoingAsync` at `:276-284` only calls `Outgoings.Remove(row)`). Since both the VM and the row are transient, GC cleans up, but only if `Outgoings` ever drops its reference — which it does. So no leak today, but if anything captures the row (e.g. a future "undo delete" stash), it will leak. And the async-void issue is real regardless.

**Fix:**

- Wrap the body in `try/catch` and surface failures via a `[ObservableProperty] string _rowEditError` (or a toast service).
- Prefer committing via an `EditCommand` on the row itself that the grid fires on `RowEditEnding`, not `PropertyChanged` per cell. See §4 refactoring wishlist.
- If keeping the current shape, at minimum: change signature to `private async Task OnRowChangedAsync(...)` + subscribe via `row.PropertyChanged += (s, e) => _ = OnRowChangedAsync(s, e);` — still fire-and-forget but the intent is explicit.

### 3.3 🔴 Dark theme resource drift

**File:** `App.xaml:9`

```xml
<ui:ThemesDictionary Theme="Light" xmlns:ui="..." />
```

The app's merged resource dictionary is hard-coded to the Light theme dictionary. `SettingsWindow.ApplyTheme` (`SettingsWindow.xaml.cs:19-28`) calls `ApplicationThemeManager.Apply(ApplicationTheme.Dark)` on save — which WPF-UI will switch the *colours* for (window backdrop, accent surfaces), but the `ThemesDictionary` already merged into the app's resources on startup stays at the Light dictionary. Lepoco's own samples (and their docs) use `ApplicationThemeManager` as the single source — once the theme manager is wired, the `ThemesDictionary Theme="Light"` declaration is either redundant or actively wrong depending on how the runtime resolves conflicts.

**Repro risk:** Switch theme to Dark → save → reload → some controls (TextBlocks with hard-coded text colours inherited from the Light dictionary, and the Mica backdrop) render against a dark chrome with light-theme text, or vice versa. The profile-accent dot (`MainWindow.xaml:29`) is a raw hex brush and will stay visible in either theme, so the user's accent colour survives — but the surrounding card chrome may clash.

**Fix:** Drop `Theme="Light"` from `App.xaml` (let `ApplicationThemeManager` own it fully), or use `ApplicationThemeManager.GetAppTheme()` on startup and swap merged dictionaries to match. Cheaper is the former. Verify dark-mode renders on all 3 windows before closing Phase 1.

### 3.4 🟡 `CurrentMonthViewModel` is now a god-VM

**File:** `CurrentMonthViewModel.cs:28-73`

23 `[ObservableProperty]` fields (up from 16 at prior review). 10 `[RelayCommand]` methods. 4 `partial void OnXChanged` callbacks wiring `CanExecute`. The VM owns: month header editing, quick-add spend, quick-add outgoing, rollover banner state, wage visibility toggle, close-month, allowance-engine outputs, category rollups, row-edit propagation for two grids. 388 lines.

Is the cohesion real? Partially. All these properties *do* sit on the same screen — the user wants Ctrl+N spend, month-header edit, and allowance re-computation to feel like one coherent surface. Splitting at the data boundary (spend / outgoing / header / allowance) would bring coordination complexity (event-passing between child VMs to trigger `Recalculate`). So I don't think it's *strictly* a god-VM — it's a VM for a dense single-page dashboard that happens to have 8 independent input widgets.

But there is a real smell: the allowance-engine outputs (`GrandTotal`, `AllowedSpendPerDay`, `OverUnder`, `ExtraSpendToSave`, etc. — 10 fields) are deterministic projections of 3 inputs (outgoings sum, transactions sum, today). They should live on a `record AllowanceSnapshot(decimal GrandTotal, decimal AllowedSpendPerDay, ...)` that `Recalculate` returns atomically. The VM just exposes `[ObservableProperty] AllowanceSnapshot _allowance = ...;` and XAML binds to `Allowance.AllowedSpendPerDay`. That removes 9 `[ObservableProperty]` fields from the VM and makes `Recalculate` trivially testable (it's now a pure function returning a snapshot).

**Fix:** Extract `AllowanceSnapshot` record at start of Phase 2, before the import wizard adds a "projected end-of-month" field or two and the count hits 30.

### 3.5 🟡 Service-locator pattern is now endemic

**File:** `CurrentMonthViewModel.cs` — 18 `GetRequiredService<T>` call sites

Was 4 at prior review, now 18. Every new command does the `_sessions.Current!.Services.GetRequiredService<IXxxRepository>()` dance. Three compounding problems:

1. **Hidden dependency surface.** Looking at the ctor at `:17-20`, a reader sees `ProfileSessionManager`, `TimeProvider`, `BudgetCalculator`. They have no idea the VM actually uses `IMonthRepository`, `IMonthlyOutgoingRepository`, `ITransactionRepository`, `ICategoryRepository`, `AppDbContext`, and `MonthService` — all routed through the service-locator dodge.

2. **Untestable.** To unit-test `Recalculate` with a seeded month you need a full `ProfileSessionManager` with a real scope with real repositories. A mocked DI scope is technically possible but nobody ever does it well.

3. **`_sessions.Current!` null-forgiving everywhere.** 15 instances of the `!` operator just in this file. Every one is a runtime "trust me" in a place the code *could* be called before a session exists (pre-boot test harness, profile-switch-mid-flight).

**Fix (for Phase 2, not Phase 1):** Register the VM inside the `ProfileSession` scope, not the root. Then constructor-inject `IMonthRepository` etc. directly. The VM becomes unit-testable with `Moq` + `Verify`. This was the same recommendation as prior review 3.5; urgency has gone up. See §4.

### 3.6 🟡 `MonthlyOutgoingRow.ApplyWageMask` mutates view state in a model class

**File:** `CurrentMonthViewModel.cs:405-411`

```csharp
public void ApplyWageMask(bool wageVisible)
{
    IsMasked = IsWage && !wageVisible;
    DisplayAmount = IsMasked
        ? "••••"
        : Amount.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
}
```

The row is itself an `ObservableObject`. `DisplayAmount` is a separate `[ObservableProperty]` from `Amount`, maintained by imperative recomputation. Every time `Amount` changes, `OnAmountChanged` (`:413-414`) calls `ApplyWageMask(!IsMasked || !IsWage)` — which has a confusing double-negation (the caller computes the condition for whether wage is currently visible, and passes it back). Cognitively expensive.

The cleaner WPF pattern is a multi-binding converter: bind the XAML `DataGridTextColumn.Binding` to a multi-converter taking `Amount` + `IsWage` + `IsWageVisible` (on the parent VM) and producing the display string. The row model holds domain data only (`Name`, `Amount`, `CategoryId`). XAML handles presentation. The bonus is that toggling `IsWageVisible` on the VM re-renders all rows for free — no need to iterate and call `ApplyWageMask` on each (currently done in `ToggleWage` at `:185`).

**Fix:** Convert to XAML `MultiBinding` with a `WageMaskConverter` class. Strip `DisplayAmount` and `IsMasked` off `MonthlyOutgoingRow`. ~30 lines of code moved from VM/model to converter + XAML.

### 3.7 🟡 `KeyBinding` in code-behind is fine *here*, but ...

**File:** `CurrentMonthView.xaml.cs:25-27`

```csharp
InputBindings.Add(new KeyBinding(
    new RelayKeyCommand(() => QuickAddPayeeBox.Focus()),
    new KeyGesture(Key.N, ModifierKeys.Control)));
```

Wiring a `KeyBinding` in code-behind is not *always* a smell — here, the target is a `TextBox` identified by `x:Name="QuickAddPayeeBox"` in the same `CurrentMonthView.xaml:239`. The command is focus-a-named-element, which XAML can't do cleanly without `FocusManager.FocusedElement` gymnastics. The code-behind form is appropriate.

The smell is `RelayKeyCommand` itself (`:31-36`) — a single-use throwaway `ICommand` type inside this file. It's hoisted to file-scoped internal. A cleaner form is `CommunityToolkit.Mvvm`'s `RelayCommand.Create(() => ...)` (if it exists — otherwise this file-scoped type is fine).

Also: the whole thing conflates UI focus behaviour with keyboard shortcut wiring. If Phase 2 adds Ctrl+E for "edit header", you'll accumulate more of these. Consider moving keyboard-shortcut-to-UI-focus mappings into a small `Window`-level handler / behaviour later. Not a Phase 1 blocker.

### 3.8 🟢 `AppHost.cs` registers every VM as Transient from the root scope

**File:** `AppHost.cs:44-53`

```csharp
builder.Services.AddSingleton<ProfileSessionManager>();
builder.Services.AddTransient<CurrentMonthViewModel>();  // from the ROOT scope
```

This is fine **because** the VM doesn't directly depend on scoped services in its constructor — it takes `ProfileSessionManager` (singleton) + `TimeProvider` (singleton) + `BudgetCalculator` (singleton). The DI graph resolves clean. But it's the root cause of §3.5 service-locator — the VM *can't* constructor-inject `IMonthRepository` because repositories are in the per-profile scope. Re-registering VMs inside `ProfileSession` would let them use constructor injection and the problem goes away. This is the main Phase-2 refactor (see §4 item 1).

### 3.9 🟢 `SettingsViewModel.SaveAsync` raises `ThemeChanged` even for no-change

**File:** `SettingsViewModel.cs:87`

Every save fires `ThemeChanged?.Invoke(this, Theme)`. If the user opens Settings and hits Save without touching the Theme dropdown, it still dispatches the theme-apply. WPF-UI's `ApplicationThemeManager.Apply` is idempotent so nothing bad happens — but it's noise, and if anyone hooks `ThemeChanged` to re-layout something expensive, it bites. Guard with `if (Theme != _systemSettings?.Theme) ThemeChanged?.Invoke(...)`.

---

## 4. Refactoring wishlist for start of Phase 2 (max 3)

### 1. Register VMs inside `ProfileSession`, not the root (kills service-locator)

**Why now:** The import wizard will add `StagedImportViewModel`, `ReviewImportViewModel`, `PayeeRuleEditorViewModel` — each with ~5 scoped dependencies. If they all copy `CurrentMonthViewModel`'s pattern, you'll have ~40 `GetRequiredService<T>` calls by end of Phase 2 and nothing testable.

**How:**
```csharp
// ProfileSession.cs — add a VM registration pass in the scope builder.
// AppHost.cs — drop AddTransient<CurrentMonthViewModel>() from root.
// Resolve via _sessions.Current.Services.GetRequiredService<CurrentMonthViewModel>()
// — one service-locator call at the boundary, none inside the VM.
```

Effort: half-day for `CurrentMonthViewModel`, then template for every future VM.

### 2. Extract `AllowanceSnapshot` record + make `Recalculate` pure

**Why now:** `Recalculate` (`CurrentMonthViewModel.cs:351-372`) is the only place 9 observable properties update together. Right now it's an imperative setter-chain. If the import wizard adds "Projected end-of-month balance after imports", you'd add a 10th setter. Extract a `record AllowanceSnapshot` and return one; VM exposes it as a single `[ObservableProperty]`. Ledger becomes:

```csharp
public void Recalculate()
{
    if (Month is null) { Allowance = AllowanceSnapshot.Empty; return; }
    var inputs = new BudgetInputs(...);
    Allowance = _calc.ProjectAll(inputs);  // one call, one assignment
}
```

XAML binds to `Allowance.AllowedSpendPerDay`. Testability is now trivial: `_calc.ProjectAll(knownInputs).Should().Be(expected)`.

Effort: 1–2 hours. Pure refactor, no behavioural change.

### 3. Row-edit via `DataGrid.RowEditEnding` + repository, not per-property `PropertyChanged`

**Why now:** `OutgoingRowChanged` / `TransactionRowChanged` are `async void` + per-cell + do one DB write per keystroke. The cleaner WPF pattern is `RowEditEnding`: save-on-commit, not save-on-property-change. Benefits:

- One DB write per user intent (not 4 for a full row).
- Proper `Task` return so the crash path routes through the global exception handler.
- No event-subscription lifecycle concern (the row doesn't need to subscribe to anything; the grid fires the event).

Pattern:
```xml
<DataGrid RowEditEnding="OnRowEditEnding" ...>
```
```csharp
private async void OnRowEditEnding(object? s, DataGridRowEditEndingEventArgs e) { ... }
```
That's still `async void` — but now it's a WPF-owned event, not a per-property subscription, and the cleanup is zero-effort (no unsubscribe needed, the row goes out of scope with the grid). Wrap in try/catch → toast.

Effort: 2 hours; replaces 40 lines of VM code.

---

## 5. The ONE thing I'd do differently next time

**Write `CurrentMonthViewModel` tests before adding CRUD, not after.**

Prior review flagged (Risk C) "before committing any Import work, write at least `CurrentMonthViewModel_Recalculate_handles_empty_month` and `CurrentMonthViewModel_LoadAsync_sets_NeedsMonthCreated_when_no_open_month`. 45 minutes of work." That didn't happen. `PiggyBank.App.Tests` still has zero test classes (only the `GlobalUsings.g.cs` and `AssemblyInfo.cs` in `obj/`). In the meantime the VM went from 16 observable properties + 5 commands to 23 + 10, and the `Recalculate` surface picked up a `_profileSettings?.BufferPerDay ?? 10m` fallback that *is* reachable from reality (profile without settings row? — it's created in `ProfileAdminService.CreateAsync:86-92` so supposedly never, but who audits that?).

If `Recalculate` had a test suite when Week 3 started, the `AllowanceSnapshot` extraction (§4 item 2) would be a one-commit refactor with a green test bar proving no behaviour change. Without tests, it becomes a "scary" refactor — which is why it'll keep getting deferred and the VM keeps growing.

The rule for Phase 2: **every new `[RelayCommand]` ships with at least one test**, even if it's a smoke test (`command.ExecuteAsync(...).Should().NotThrow()` with mocked deps). That's 10 minutes per command and it stops the dashboard VM being a no-go zone for future refactoring.

Secondary thing I'd do differently: **I'd lock `CanUserAddRows="False"` behind an architectural comment** explaining *why* the in-grid add was rejected. The quick-add form replaced it correctly — but a future developer pulling up this XAML will see `CanUserAddRows="False"` on two grids and think "oh, the DataGrid is broken, let me turn that on". Two-line comment saves them an hour.

---

## 6. Portable-core compliance — unchanged from prior review

Core grep for `DateTime.UtcNow` / `DateTime.Now` / `System.Windows.*` / `Microsoft.EntityFrameworkCore` returns only doc-comment mentions (no runtime references). Core `.csproj` remains zero `PackageReference`, zero `ProjectReference`. Data project uses `TryAddSingleton(TimeProvider.System)` so deterministic-time contract is preserved even if a caller bypasses `AppHost`. Rule still green.

---

## 7. Multi-tenancy safety rails — now fully green

| # | Rail | Status | Notes |
|---|---|---|---|
| 1 | `ProfileOwnedEntity` base | PASS | 6 entities inherit. |
| 2 | `ITenantContext` scoped, locking | PASS | `MutableTenantContext.cs:22-36` throws on re-`Set`. |
| 3 | Reflection-driven query filter | PASS | `AppDbContext.cs:154-160`. |
| 4 | Filter reads `this.CurrentProfileId` | PASS | `:168-169`; doc comment at `:18-23` is still there. |
| 5 | `TenantStampInterceptor` | PASS | Both sync + async overrides (`TenantStampInterceptor.cs:17-32`). |
| 6 | **DB-level FK/NOT NULL** | **NOW PASS** | `Migrations/20260422212515_AddTenantForeignKeys.cs` added all four. |
| 7 | `IgnoreQueryFilters` use-sites | PASS | 4 in `ProfileAdminService` (lines 27, 38, 103, 116), 2 in tests. Zero elsewhere. |

6/7 → 7/7. The tenancy story is the strongest part of the architecture and now has no loose ends.

---

## 8. Relevant files

- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Dashboard\CurrentMonthViewModel.cs` — god-VM candidate, service-locator endemic, async-void row handlers (§3.2, §3.4, §3.5, §3.6)
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Dashboard\CurrentMonthView.xaml` — duplicated `IsFuture` column (§3.1), lines 206-211
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\App.xaml` — hard-coded `Theme="Light"` ThemesDictionary (§3.3), line 9
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Settings\SettingsWindow.xaml.cs` — `ApplyTheme` via `ApplicationThemeManager` (§3.3)
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Dashboard\CurrentMonthView.xaml.cs` — Ctrl+N key binding (§3.7), acceptable
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Hosting\AppHost.cs` — root-scope VM registration (§3.8, §4.1)
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\App.xaml.cs` — global exception handlers (prior 🔴 closed), theme startup apply
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Entities\Profile.cs` — prior 3.2 🔴 closed
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Migrations\20260422212515_AddTenantForeignKeys.cs` — prior 3.1 🔴 closed
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Profiles\ProfileAdminService.cs` — `TimeProvider` wiring, only `IgnoreQueryFilters` site
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\AppDbContext.cs` — FK coverage now complete (lines 86-148)
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Tenancy\TenantLeakTests.cs` — reflection-driven + `NotEmpty` / `OnlyContain` (prior 3.3 🟡 closed)
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Tenancy\TestDbFactory.cs` — uses `Migrate()` not `EnsureCreated()` (prior 3.8 🟢 closed)
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.App.Tests\` — **still empty** (§5, Risk C from prior review unaddressed)
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Migrations\20260422204815_SeedCategoryDefaultEnabled.cs` — prior 3.9 🟢 still open (empty up/down)
