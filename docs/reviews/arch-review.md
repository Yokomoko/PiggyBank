# Architecture Review — End of Week 2

Reviewer: Claude (senior architecture reviewer)
Date: 2026-04-22
Scope: `C:\ScmWorkspaces\PiggyBank` — 8 projects, 65 tests, commits on `main`
Benchmarks: `C:\Users\achapman\PiggyBankWPF\03-architecture.md` (§13 tenancy addendum is authoritative), `PiggyBank-WPF-Spec.md` §2.0 portable-core, §2.1b tenancy rules.

---

## 1. Verdict

**Ready for Week 3, with 2 small must-fix items that are 30-minute jobs.** The architecture genuinely matches the plan. The portable-core rule is intact, the seven tenancy safety rails are all real (not just declared), and the rollover semantics match the 2026-04-22 "manual, never automatic" decision exactly. Two narrow gaps are flagged below (FK coverage on new tables, `DateTime.UtcNow` leak into `Profile`) but neither blocks Week 3's import work. The `RepositoryTenantLeakTests` coverage is hand-rolled rather than reflection-driven — that's a Week 3 chore worth doing alongside the new Import repositories.

---

## 2. Strengths

- **Portable-core rule is honoured to the letter.** `PiggyBank.Core.csproj` has zero package references and no `ProjectReference` entries. Grep of the source confirms no `System.Windows.*`, no `INotifyPropertyChanged`, no `DbContext`, no `Microsoft.EntityFrameworkCore`. The only `DbContext` reference anywhere in Core is a single doc comment in `ITenantContext.cs:10`. This is textbook.
- **Query-filter bug was understood and neutralised.** `AppDbContext.cs:18–23` has an explicit comment explaining why the filter reads `this.CurrentProfileId` (instance property on the DbContext) rather than capturing `_tenant` via `Expression.Constant`. The comment names the precise failure mode: "EF would bake the first context's tenant into the shared model". The `TenantLeakTests` prove the fix works. This is senior-level work.
- **Manual-rollover decision lives in tests, not just docs.** `MonthServiceTests.Create_does_not_auto_rollover_even_from_closed_prior` (line 52) is exactly the test that future regressions will trip. The VM surface (`SuggestedRollover` + `RolloverBannerVisible`) makes the UX intent testable too. Zero risk of the rule drifting.
- **The pure `BudgetCalculator` is genuinely pure.** Takes a `TimeProvider` but doesn't actually read from it yet — every method takes all inputs explicitly via a `BudgetInputs` record. 32 unit tests cover boundary conditions including zero-day, negative grand total, sign-flipping. This is the spec's §7 built as promised.
- **DI scope hygiene is correct.** `AppDbContext` is scoped (default from `AddDbContext`), `MutableTenantContext` is scoped, `TenantStampInterceptor` is scoped and resolved via the factory lambda so each scope gets a fresh one wired to its own tenant. `ProfileSessionManager` (singleton) owns only the root provider and the current `ProfileSession`, which in turn owns its scope. Switching profiles triggers `Dispose()` + new scope — no state leak path exists.

---

## 3. Findings

### 3.1 🔴 Missing `HasOne<Profile>().HasForeignKey(ProfileId)` on four tenant tables

**File:** `src/PiggyBank.Data/AppDbContext.cs:92–132` (and migration `Migrations/20260422204645_AddLedgerAndOutgoings.cs`)

`Category` (line 86–90) and `ProfileSettings` (line 73–77) correctly declare a FK from `ProfileId` to `Profiles.Id` with `DeleteBehavior.Cascade`. But `RecurringOutgoing`, `Month`, `MonthlyOutgoing` and `Transaction` have no `HasOne<Profile>()` relationship configured at all — their `ProfileId` columns are `nullable: false` but carry no FK constraint in the generated SQL. You can confirm this in the migration file: only `FK_Categories_Profiles_ProfileId` and `FK_ProfileSettings_Profiles_ProfileId` exist.

**Why it matters:** The spec's §13 safety rail #5 says "DB-level `CHECK` / FK constraint on `ProfileId`". Right now those four tables would silently accept an orphan `ProfileId` if anything bypassed the interceptor (raw SQL, bad migration, future SQL-runner admin pane). Also, deleting a profile won't cascade to `Month`/`Transaction`/etc. rows — the Phase-3 hard-delete workflow assumes cascade and will leave zombie rows.

**Suggested fix:** Add the same `HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade)` clause to each entity config in `OnModelCreating`, then scaffold a new migration. Ten-line fix, one migration.

```csharp
b.Entity<Month>(e =>
{
    // existing...
    e.HasOne<Profile>()
        .WithMany()
        .HasForeignKey(x => x.ProfileId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Also worth: SQLite doesn't enforce `NOT NULL` on a `Guid.Empty` — consider a `HasCheckConstraint("CK_Month_ProfileId", "ProfileId != X'00000000000000000000000000000000'")` on each tenant table as a real backstop.

### 3.2 🔴 `DateTime.UtcNow` in Core (`Profile.cs:16`)

**File:** `src/PiggyBank.Core/Entities/Profile.cs:16`

```csharp
public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
```

**Why it matters:** The spec's §2.0 portable-core rule explicitly says "Never `DateTime.Now` inside Core — tests and server-hosted variants need deterministic time." This is the only leak in Core right now but it's the exact shape of the rule you made for yourself. Any Core test that calls `new Profile()` gets a non-deterministic timestamp; any Blazor-WASM port that has only the root clock available can't override it.

**Suggested fix:** Drop the default; let `ProfileAdminService.CreateAsync` stamp it via an injected `TimeProvider`. One-line change in Core, three lines in the service.

```csharp
// Profile.cs
public DateTime CreatedAtUtc { get; set; }

// ProfileAdminService.cs (take TimeProvider via ctor)
var profile = new Profile
{
    DisplayName = displayName.Trim(),
    ColourHex = colourHex,
    IconKey = iconKey,
    CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
};
```

Same fix applies to the two `DateTime.UtcNow` calls in `ProfileAdminService.cs:104, 117` (`ArchivedAtUtc`, `LastOpenedAtUtc`) — they're in Data, which is less strict but the `TimeProvider` is already available in DI, so no reason not to use it.

### 3.3 🟡 `RepositoryTenantLeakTests` is hand-rolled, not reflection-driven

**File:** `tests/PiggyBank.Data.Tests/Tenancy/RepositoryTenantLeakTests.cs:34–51`

The test explicitly instantiates each repository:

```csharp
var cats = await new CategoryRepository(db.Context).ListAsync();
var recurring = await new RecurringOutgoingRepository(db.Context).ListAsync();
var months = await new MonthRepository(db.Context).ListAsync();
// ...
```

**Why it matters:** Your own brief asked "if a dev adds a new `ProfileOwnedEntity` tomorrow, will the existing tests catch a tenant leak without code changes?" Short answer: **`TenantLeakTests` yes, `RepositoryTenantLeakTests` no.** The DbSet-level test (`TenantLeakTests.cs:39–69`) reflects over `typeof(AppDbContext).GetProperties()` and will cover any new `DbSet<T>` automatically — that's the primary gate. But the repository-level test adds explicit `new XxxRepository(db.Context).ListXxxAsync()` lines, so if Phase 2 adds `PayeeRuleRepository.ListAsync()` and the author forgets to filter, this test won't trip.

The good news: the DbSet test does catch leaks at the query-filter layer, which is where they'd actually happen. The repository-level test is belt-and-braces. So this is 🟡 not 🔴 — but you should either:

- (a) Make `RepositoryTenantLeakTests` reflect over `typeof(ICategoryRepository).Assembly.GetTypes().Where(t => t.GetInterfaces().Any(i => i.Name.EndsWith("Repository")))` and drive an `IEnumerable`-returning method by convention, or
- (b) Delete it (the DbSet-level test covers the same ground) and add a CI-level reminder that new repos need a leak test added.

I'd go with (a) before Week 3 because the Import work will add repositories for `PayeeRule`, `_ImportLog`, `_ImportRow` and it'll be cheaper to generalise the test now than whack-a-mole later.

### 3.4 🟡 `MonthRepository.ListAsync()` has no `MonthId` filter — over-fetches

**File:** `src/PiggyBank.Data/Repositories/MonthRepository.cs:8–9`

```csharp
public async Task<IReadOnlyList<Month>> ListAsync(CancellationToken ct = default)
    => await db.Months.OrderByDescending(m => m.PeriodStart).ToListAsync(ct);
```

**Why it matters:** After 11 years of imported history, this returns ~45 `Month` rows. That's fine today. But `TransactionRepository.SumByCategoryForMonthAsync` (line 34) materialises `await db.Categories.ToDictionaryAsync(...)` every call — also fine at 30 categories. Both are 🟡 because they're correct; they just won't scale if you add multi-year analytics views in Phase 2. Worth flagging the pattern so the next time someone writes an "all rows" query they think about paging.

**Suggested fix:** Leave these alone for Phase 1, but add `ListAsync(int take, int skip, ...)` overloads before the history list view ships. For `SumByCategoryForMonthAsync`, a single SQL projection with a `LEFT JOIN` avoids the dictionary round-trip — can wait for Phase 2.

### 3.5 🟡 `CurrentMonthViewModel` resolves services off `_sessions.Current.Services` manually

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:63–67`

```csharp
var scope = _sessions.Current.Services;
var months = scope.GetRequiredService<IMonthRepository>();
var outgoings = scope.GetRequiredService<IMonthlyOutgoingRepository>();
var txs = scope.GetRequiredService<ITransactionRepository>();
var cats = scope.GetRequiredService<ICategoryRepository>();
```

**Why it matters:** This is a service-locator pattern, which the spec §4 warns against ("Never use `ServiceLocator` anti-pattern inside ViewModels"). It works today because the VM is registered as Transient and the session scope is set before the VM is resolved. But:

1. It couples the VM to `ProfileSessionManager` specifically rather than to the repositories it needs.
2. It prevents `CurrentMonthViewModel` from being unit-tested without wiring up a real `ProfileSessionManager`.
3. If a developer later registers the VM in the root scope rather than the profile scope, the VM will silently pull the wrong `DbContext`.

The reason it was done this way is real: the VM is registered at the root DI container, but the repositories live in the per-profile scope. You can't just constructor-inject `IMonthRepository` into a root-scoped VM or it'll try to resolve from the root scope (no tenant = exception).

**Suggested fix:** Register VMs inside the `ProfileSession` scope rather than the root. The VM then gets constructor-injected repositories bound to its own session, mockable for tests. Pattern:

```csharp
// AppHost.cs — move VM registrations into the scope, not the root
builder.Services.AddSingleton<ProfileSessionManager>();
// drop: builder.Services.AddTransient<CurrentMonthViewModel>();

// ProfileSession.cs — register VMs inside the scope builder
public ProfileSession(IServiceProvider root, Guid profileId)
{
    _scope = root.CreateScope();
    // existing tenant setup
    // no VM registration needed because the scope inherits the root's services
    // — repositories already scoped will resolve correctly
}

// Then VM constructor becomes:
public CurrentMonthViewModel(
    IMonthRepository months,
    IMonthlyOutgoingRepository outgoings,
    ITransactionRepository transactions,
    ICategoryRepository categories,
    MonthService monthService,
    TimeProvider clock,
    BudgetCalculator calc) { ... }
```

Resolve from `_sessions.Current.Services.GetRequiredService<CurrentMonthViewModel>()` when you need one. This removes both the service-locator smell and the hidden coupling.

Not a Week 3 blocker — the current shape is safe because only one VM exists. Fix before Week 5 when you have 5+ VMs, or you'll be refactoring under pressure.

### 3.6 🟡 `ProfileSessionManager.OpenProfile` is not thread-safe and silently dumps previous session

**File:** `src/PiggyBank.App/Profiles/ProfileSessionManager.cs:37–41`

```csharp
public void OpenProfile(Guid profileId)
{
    _current?.Dispose();
    _current = new ProfileSession(rootProvider, profileId);
}
```

**Why it matters:** If two UI threads call `OpenProfile` concurrently (bug in a future switch-profile workflow), the second caller's `Dispose()` could race with the first caller's `new ProfileSession`. Today it's fine because only `OnStartup` calls it. When "Switch profile" menu item lands in Week 4–5, this becomes reachable. Also, there's no `SessionChanged` event — the VMs currently capture `_sessions.Current` at method-call time, which works but couples them to "always resolve fresh".

**Suggested fix:** Add a lock (or a simple `SemaphoreSlim`) and an event. Minor.

```csharp
private readonly object _gate = new();
public event EventHandler? SessionChanged;

public void OpenProfile(Guid profileId)
{
    lock (_gate)
    {
        _current?.Dispose();
        _current = new ProfileSession(rootProvider, profileId);
    }
    SessionChanged?.Invoke(this, EventArgs.Empty);
}
```

### 3.7 🟡 `ProfileSession` has a confusing dual-constructor pattern

**File:** `src/PiggyBank.Data/Tenancy/ProfileSession.cs:18–38`

Two constructors: public one for normal profile use, private one that takes a pre-built scope used only by `AdminScope`. `AdminScope` itself creates a scope, configures the tenant, then passes the same scope into the private ctor, which means the scope is configured outside the session object. The `AdminScope` returns a session with `ProfileId = Guid.Empty`, which is a sentinel value — and `Guid.Empty` is also the value the interceptor compares against to detect "no profile" (`TenantStampInterceptor.cs:44`). That's two different meanings for the same zero-GUID.

**Why it matters:** Not broken — works correctly — but the semantics are fragile. A developer reading `session.ProfileId == Guid.Empty` can't tell whether they're in admin scope or in a buggy "profile never set" state. `IsAdminScope` on `ITenantContext` exists for this reason, so it's internally discoverable; externally (via the session) it's not.

**Suggested fix:** Expose `bool IsAdminScope => ProfileId == Guid.Empty;` on `ProfileSession`, or return `Guid?` instead. Five-line change.

### 3.8 🟢 `TestDbFactory.cs:62` has a dead line

**File:** `tests/PiggyBank.Data.Tests/Tenancy/TestDbFactory.cs:62`

```csharp
Tenant.GetType(); // retained only for diagnostics; this TestDb's Tenant is the admin one
```

A no-op — just reading a property value and discarding it. Either delete or make it do something explicit (log? assert?).

### 3.9 🟢 `SeedCategoryDefaultEnabled` migration is empty

**File:** `src/PiggyBank.Data/Migrations/20260422204815_SeedCategoryDefaultEnabled.cs`

An empty `Up()` and `Down()`. Presumably `dotnet ef migrations add` was run after adding the `DefaultEnabled` property but the property was already in the previous migration's snapshot? Worth checking. If the migration is genuinely a no-op, delete it rather than leaving it as confusing evidence.

### 3.10 🟢 `AppSettings.Id` defaults to `1` but the column is `Autoincrement`

**File:** `src/PiggyBank.Core/Entities/AppSettings.cs:9` + `Migrations/20260422204026_InitialCreate.cs:19`

The entity says `Id = 1` (singleton expectation) but the table is declared with `Sqlite:Autoincrement`. If the first insert is done via EF and the `Id` is seen as `1` (non-default for `int`), EF should preserve it. But if you're ever forced to re-insert (corrupt DB recovery), the auto-increment counter may have moved on and EF's PK conflict detection will refuse. Low risk, but inconsistent with the "singleton row" intent.

**Suggested fix:** Either set `.ValueGeneratedNever()` in the entity config (kills auto-increment, preserves the singleton contract) or stop asserting `Id = 1` in the entity and let EF assign. The former matches the spec §6 comment "`AppSettings` — singleton row".

### 3.11 🟢 `CurrentMonthViewModel` exposes 16 `[ObservableProperty]` fields — candidate to split

**File:** `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs:25–55`

`Outgoings`, `Transactions`, `CategoryRollups`, `AvailableCategories`, `Month`, `PeriodLabel`, `IsBusy`, `IsWageVisible`, `NeedsMonthCreated`, `SuggestedRollover`, `RolloverBannerVisible`, plus 12 allowance-engine fields (Total, GrandTotal, MonthlyTotal, DaysToNextPayday, DaysSincePayday, AllowedSpendPerDay, AllowedMonthlyRemaining, AllowedWeeklyRemaining, SpentPerDayToDate, EstimatedSpend, OverUnder, ExtraSpendToSave). That's almost every cell in the spreadsheet's B-column on one class.

It works, and the tests can cover it. But the next feature ("close month" ceremony, "quick-add spend") will likely grow this VM to 25+ properties. Consider splitting into `CurrentMonthViewModel` (month-level) + `AllowanceSnapshot` (computed, passed to a child `AllowancePanelViewModel`). Can wait for Week 5 when the dashboard is real.

---

## 4. Portable-core compliance table

| Project | TargetFramework | UI refs | Threading refs | I/O refs | EF Core refs | Async I/O only | Verdict |
|---|---|---|---|---|---|---|---|
| PiggyBank.Core | `net10.0` | None | None | None | None (1 comment mentions DbContext) | N/A — no I/O | PASS |
| PiggyBank.Data | `net10.0` | None | None | EF Core + SQLite only | Yes, proper | Yes (all methods `async Task<T>`, `CancellationToken` on every async method) | PASS |
| PiggyBank.Import | `net10.0` | None | None | ClosedXML (takes `Stream`) | None | Tests only have `.GlobalUsings` today — nothing to audit yet | PASS (trivially — project is empty) |
| PiggyBank.App | `net10.0-windows` | WPF + Wpf.Ui | WPF Dispatcher (via `Loaded += async`) | `%LocalAppData%\PiggyBank\app.db` | Yes | Yes | PASS — correct single Windows project |
| Core.Tests | `net10.0` | None | None | None | None | N/A | PASS |
| Data.Tests | `net10.0` | None | None | SQLite `:memory:` | Yes | Yes | PASS |
| Import.Tests | `net10.0` | None | None | ClosedXML (unused today) | None | N/A | PASS (empty) |
| App.Tests | `net10.0-windows` | WPF | N/A | N/A | N/A | N/A | ACCEPTABLE — spec says ViewModel tests go here; empty today |

**Overall:** rule compliant. The `DateTime.UtcNow` in `Profile.cs` (finding 3.2) is the only technically-leaky bit.

---

## 5. Multi-tenancy safety rail checklist

| # | Rail (per §13) | Status | Evidence |
|---|---|---|---|
| 1 | `ProfileOwnedEntity` base class with `ProfileId Guid NOT NULL` | 🟢 PASS | `ProfileOwnedEntity.cs:9–13` defines the base; 7 entities inherit (`Category`, `RecurringOutgoing`, `Month`, `MonthlyOutgoing`, `Transaction`, `ProfileSettings`; only 6 of spec's 11 — but the missing ones are Phase 2 (`Debt`, `DebtSnapshot`, `SavingsProjection`, `SavingsRateChange`, `CreditScore`) and will drop straight in) |
| 2 | `ITenantContext` single scoped service exposed via `MutableTenantContext` | 🟢 PASS | `ITenantContext.cs` in Core; `MutableTenantContext.cs` in Data implements + locks after first `Set` (`MutableTenantContext.cs:22–27`); `DependencyInjection.cs:18–19` registers both correctly as scoped with forwarding. |
| 3 | EF Core global query filter applied automatically to every `ProfileOwnedEntity` via reflection | 🟢 PASS | `AppDbContext.cs:138–144` iterates `b.Model.GetEntityTypes()` and applies `HasQueryFilter` to every type assignable to `ProfileOwnedEntity`. Reflection-driven — zero maintenance when Phase 2 entities land. |
| 4 | Filter uses `this.CurrentProfileId`, not `Expression.Constant(_tenant)` | 🟢 PASS | `AppDbContext.cs:150–159` — `Expression.Constant(this)` then `Expression.Property(..., nameof(CurrentProfileId))`. Instance property read, not captured constant. Doc comment on `AppDbContext.cs:18–23` calls out the distinction explicitly. |
| 5 | `SaveChanges` interceptor auto-stamps on insert, blocks mutation | 🟢 PASS | `TenantStampInterceptor.cs:38–67`; `Added` → stamp from `tenant.CurrentProfileId` (throws if no scope), `Modified` → throw if `ProfileId` changed. Three interceptor tests at `TenantStampInterceptorTests.cs` cover all three paths. |
| 6 | DB-level FK / NOT NULL on `ProfileId` | 🟡 PARTIAL | `Categories` and `ProfileSettings` have `FK_..._Profiles_ProfileId` (migration line 80–86, 103–109). `Month`, `MonthlyOutgoing`, `RecurringOutgoing`, `Transaction` have `nullable: false` but **no FK constraint**. See finding 3.1. |
| 7 | `IgnoreQueryFilters` only used by `ProfileAdminService` + tests | 🟢 PASS | Grep: 4 call sites in `ProfileAdminService.cs` (production), 2 call sites in tests. Zero other occurrences. The analyser `MM0001` mentioned in spec is not yet implemented — CI grep is sufficient for now. |
| (7b) | `ProfileSession` disposes scope on switch | 🟢 PASS | `ProfileSession.cs:40–45` — `Dispose()` disposes `_scope`. `ProfileSessionManager.OpenProfile` (`ProfileSessionManager.cs:37–41`) disposes `_current` before creating new. Runs per-switch. |

**Overall:** 6/7 pass, 1 partial (FK coverage — finding 3.1). Fix 3.1 and the rail list is fully green.

---

## 6. Three risks to flag before moving on

### Risk A — Phase 2 debt-snapshot import will add 5 entities, some with unusual relationships

The current design is clean because every tenant entity maps one-to-one to a row you create. When Phase 2 lands, `DebtSnapshot` has a composite uniqueness (`DebtId + MonthId`), `SavingsProjection` rows derive from a generator (`SavingsRateChange` breakpoints feeding projection rows), and `Debt` has `ClosedAtUtc` that acts as a soft-delete vector. I'd worry that the reflection-based query filter keeps working but someone writes an `.Include(d => d.Snapshots)` without realising the filter doesn't cascade through navigation properties in all cases. Mitigation: add an integration test now that seeds two profiles with `DebtSnapshot` data and verifies `Include` doesn't leak. This catches the regression before Phase 2 hits.

### Risk B — Single-user file locking when the app opens a second instance

Spec §13.9 risk #8 ("Rollover double-fire if app opened twice") mentions an app-level mutex. None exists today. Two concurrent `AppDbContext`s against one SQLite file is fine for reads, but the rollover transaction + `Database.MigrateAsync` at startup can race if the user double-clicks the shortcut. `%LocalAppData%\PiggyBank\app.db` is one file for both profiles; a second instance will try to open it with a fresh `DbContext` scope. Mitigation: add a named `Mutex` in `App.OnStartup` before `_host.StartAsync()`. Five lines; cheap insurance.

### Risk C — The App project has no ViewModel tests yet

`PiggyBank.App.Tests` target framework is set up (`net10.0-windows`), but zero test classes exist. The `CurrentMonthViewModel` has real business logic (`Recalculate` on line 149–168 orchestrates 7 calculator calls and reads the ledger). If this ships to Week 3 without a test, and someone refactors the property ordering or the `Math.Max(0, ...)` clamps for `DaysToNextPayday`, it will silently miscalculate. Mitigation: before committing any Import work, write at least `CurrentMonthViewModel_Recalculate_handles_empty_month` and `CurrentMonthViewModel_LoadAsync_sets_NeedsMonthCreated_when_no_open_month`. 45 minutes of work; gets you past the "zero test coverage on the main screen" embarrassment before the import wizard doubles the VM's complexity.

---

## 7. Minor notes (not findings — observations for your notebook)

- The `BudgetCalculator` constructor takes `TimeProvider` but never reads it. Either use it (e.g. for a future `ComputeFromToday(...)` overload) or drop it. Dead code smells.
- `CurrentMonthViewModel.Recalculate` is currently `public` — invoked internally from `LoadAsync`. If external code ever calls it, the `BudgetInputs` won't be recomputed from the current ledger. Consider making it `private` and exposing a `[RelayCommand] RefreshCommand` instead.
- `SeedCategoryCatalog.cs` has 32 entries vs. the spec's 32 (23 original + 9 new). Matches the decisions-doc `#6 Expanded seed category list`. Niche legacy ones (Paint, Sprays, Cashpoint, Lend Money, Rent/Phone/Gym Overpayment, Wardrobe, Medication/Tablets) correctly default to `DefaultEnabled = false`.
- `PaydayCalculator` is `static` + pure, takes explicit `year`/`month`/`day` params OR `DateOnly anchor`, zero `DateTime.Now` leaks, and the bank-holiday table covers 2024–2030. Tests verify Saturday/Sunday/bank-holiday shifts, the Feb 31 clamp, and the `anchor.AddMonths` window. Exactly what the spec wanted.
- The `[ObservableProperty]` generator on `CurrentMonthViewModel` produces source-generated properties — matches spec §3 "trimming-safe code which matters if AOT or self-contained publish is used later".
- No magic strings, no God classes, no methods >60 lines. `ApplyProfileFilter` is 11 lines; `MonthService.CreateAsync` is 41 lines doing one thing.
- `nullable` annotations are consistent. Nullable fields are deliberate (`RecurringOutgoingId? Guid`, `CategoryId? Guid`, `Notes? string`) and match the spec's domain intent.

---

## 8. Recommended order for Week 3 warm-up (~90 minutes before touching Import)

1. Add the four missing FK constraints (finding 3.1) → new migration `AddProfileFKConstraints`. 20 minutes.
2. Remove the `DateTime.UtcNow` default from `Profile.cs` and wire `TimeProvider` into `ProfileAdminService` (finding 3.2). 15 minutes.
3. Refactor `RepositoryTenantLeakTests` to reflect over repository interfaces (finding 3.3). 25 minutes.
4. Write two baseline tests for `CurrentMonthViewModel` (risk C). 30 minutes.

Then start the xlsm import with a clean conscience.

---

## 9. Relevant files

- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Entities\Profile.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Entities\ProfileOwnedEntity.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Budgeting\BudgetCalculator.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Payday\PaydayCalculator.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Core\Tenancy\ITenantContext.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\AppDbContext.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\DependencyInjection.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Services\MonthService.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Profiles\ProfileAdminService.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Tenancy\MutableTenantContext.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Tenancy\TenantStampInterceptor.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Tenancy\ProfileSession.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.Data\Migrations\20260422204645_AddLedgerAndOutgoings.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Hosting\AppHost.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Dashboard\CurrentMonthViewModel.cs`
- `C:\ScmWorkspaces\PiggyBank\src\PiggyBank.App\Profiles\ProfileSessionManager.cs`
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Tenancy\TenantLeakTests.cs`
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Tenancy\RepositoryTenantLeakTests.cs`
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Tenancy\TestDbFactory.cs`
- `C:\ScmWorkspaces\PiggyBank\tests\PiggyBank.Data.Tests\Services\MonthServiceTests.cs`
