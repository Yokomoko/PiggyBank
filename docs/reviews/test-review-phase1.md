# Test Suite Review — Phase 1 MVP Audit

Produced by `apex-dotnet-plugin:Test Validator` agent 2026-04-22. Captured by the parent session since the validator's system prompt forbids direct file writes.

## 1. Verdict

**False confidence on the ViewModel / orchestration layer; genuinely strong on calculators and the tenant gate.**

`TenantLeakTests` rewrite is a real improvement — it now proves the right thing. `TestDbFactory.Migrate()` actually exercises the migration chain. But Phase 1 shipped an entire ViewModel layer (`CurrentMonthViewModel`, `SettingsViewModel`) plus per-row event propagation and quick-add persistence with **zero automated coverage**. Of the 33 missing edge cases flagged previously, roughly 5 addressed; the remainder open. Test-count-to-feature-count ratio has regressed sharply.

| Project | Tests | Verdict |
|---|---:|---|
| `PiggyBank.Core.Tests` | 44 | Unchanged since Week 2. Still ~half the catalogued cases. |
| `PiggyBank.Data.Tests` | 21 | Tenant gate stronger; breadth unchanged. |
| `PiggyBank.App.Tests` | 0 | **Empty project.** Phase 1 UI unverified. |
| `PiggyBank.Import.Tests` | 0 | Acceptable — no Phase 1 import surface. |

## 2. Prior findings status

### 2.1 FIXED — TenantLeakTests vacuous-pass hole
Rewrite at `tests/PiggyBank.Data.Tests/Tenancy/TenantLeakTests.cs:37-75` genuinely closes the hole: reflection-driven discovery, `MinimumExpectedTenantDbSets = 6` sanity check, paired `NotBeEmpty` + `OnlyContain`, throw-on-unrecognised-type seeder. A deliberately broken filter fails the test; a missing filter on a new entity also fails.

### 2.2 FIXED — TestDbFactory uses Migrate()
`tests/PiggyBank.Data.Tests/Tenancy/TestDbFactory.cs:41`. Five migrations run. **Caveat** — see §4.2: FK enforcement in SQLite `:memory:` tests not proven.

### 2.3 FIXED — ProfileAdminService ctor TimeProvider
Mechanical update only; no new behavioural coverage added for `CreatedAtUtc` being set from the clock.

### 2.4 STILL OPEN — RepositoryTenantLeakTests still vacuous-risky
`tests/PiggyBank.Data.Tests/Tenancy/RepositoryTenantLeakTests.cs:34-51`. Hardcodes 5 repos, no `NotBeEmpty` guards. New Phase 2 repo would escape silently.

### 2.5 STILL OPEN — BudgetCalculatorTests uses TimeProvider.System
`BudgetCalculatorTests.cs:8`. Works today because `_clock` is unused; foot-gun armed for first calculator method that reads it.

### 2.6 STILL OPEN — `OverUnder_positive_when_on_track_to_overshoot` name/body contradiction
`BudgetCalculatorTests.cs:128-134`. Test named "positive" asserts `-300m`. Genuine positive case has no coverage.

### 2.7 STILL OPEN — `EstimatedSpendWithBuffer` / `EstimatedLeftAfterBuffer` — zero tests
`BudgetCalculator.cs:81-88`. Public methods; zero hits in `tests/`.

### 2.8 STILL OPEN — `ProfileAdminService.TouchLastOpenedAsync` — zero tests
`ProfileAdminService.cs:111-122`. Uses `IgnoreQueryFilters`; runs from profile scope. Trivially testable with `FakeTimeProvider`; not tested.

### 2.9 STILL OPEN — `MonthService.ComputeClosingBalanceAsync` + multi-month chain + `ApplyRollover_no_prior_month`
All three prior gaps remain. `ApplyRolloverFromPriorAsync` on a first-ever month throws but the throw path has no test.

### 2.10 STILL OPEN — `SeedCategorySeeder` direct tests
No idempotency / no re-run / no mixed-case merge coverage.

### 2.11 STILL OPEN — TenantStampInterceptor missing branches
"Insert with already-set mismatching ProfileId" branch (`TenantStampInterceptor.cs:50-55`) untested. Insert-match / Modify-no-change / Delete also uncovered.

## 3. New Phase 1 surface with zero coverage (ranked by value)

### 3.1 `CurrentMonthViewModel` — P0 critical
300+ lines, 10 `[RelayCommand]` methods. Specific hotspots:
- **Quick-add spend sign-flip** at `CurrentMonthViewModel.cs:215` (`Amount = -Math.Abs(NewSpendAmount)`) — every calculation hangs off this
- **Form reset retains `NewSpendCategory`** — documented UX invariant, untested
- **`CanAddSpend`** guard logic — whitespace payee / zero amount
- `Recalculate` triggered by `AddSpendAsync` — breaks live allowance sync silently
- `LoadAsync` branches — `NeedsMonthCreated`, collections populated, `SuggestedRollover` computed
- `CreateCurrentMonthAsync` uses `PaydayCalculator.ResolvePayWindow` — untested
- `ApplyRolloverAsync` guard `SuggestedRollover is null`
- `ToggleWage` loops every row calling `ApplyWageMask`
- `SaveMonthHeaderAsync` `PeriodEnd = NextPayday.AddDays(-1)` off-by-one risk
- `CloseMonthCommand.CanExecute` tied to `IsClosed`

### 3.2 `MonthlyOutgoingRow.ApplyWageMask` — P0 privacy-critical
`CurrentMonthViewModel.cs:405-411`. Truth table has 4 states; zero tests. A `&&` → `||` regression would mask every row when wage visibility is off. Also subtle `OnAmountChanged` re-apply logic at line 413-414 untested.

### 3.3 `SettingsViewModel` — P1
- Dual-scope write (`AppSettings` admin + `ProfileSettings` profile) with no partial-failure coverage
- Insert-vs-update branch on `AppSettings` first-run
- `ThemeChanged` event fire
- **`DateTime.Now` at line 88** — violates TimeProvider rule

### 3.4 `MonthService.CloseAsync` — direct test missing
Covered transitively only. Not-found and idempotency cases untested.

### 3.5 Row-level edit propagation — P2
`OutgoingRowChanged` / `TransactionRowChanged` — property filter, null-entity guard, `RefreshRollupsAsync` + `Recalculate` side-effects all untested.

### 3.6 Ctrl+N binding
Untestable at unit level; covered by E2E (acceptable).

## 4. Specific answers to the user's questions

### 4.1 Does TenantLeakTests catch a deliberately-broken filter?
**Yes.** Three scenarios all fail as expected: remove `ApplyProfileFilter`, invert filter to always-false, add new `ProfileOwnedEntity` without a filter. The third is especially nice — you can't ship a new entity without adding a seeder case.

### 4.2 Does `TestDbFactory.Migrate()` actually cover every migration including FK enforcement?
**Partially.** Migrations run. But SQLite enforces FKs only when `PRAGMA foreign_keys = ON`. No production-path hits for PRAGMA in the Data project. EF Core may emit it per-connection but the test injects its own `SqliteConnection` via `UseSqlite(conn)` — provider-version-dependent whether the PRAGMA fires. **Risk:** `AddTenantForeignKeys`'s four FKs may not be enforced in tests; a production insert violating them would silently pass. One-line fix: a test that inserts a `Month` with a bogus `ProfileId` and expects `SqliteException`.

### 4.3 Is `ApplyWageMask` correct for all (IsWage, wageVisible) states?
**Yes by inspection.** But literally nothing pins this correctness in a test. Correct today only because the reviewer reasoned through the four states by hand.

### 4.4 Was ProfileAdminServiceTests updated beyond the ctor arg?
**No.** Only the ctor arg. No test uses `FakeTimeProvider` to pin the timestamp. `Profile.CreatedAtUtc`'s "never default to `DateTime.UtcNow`" doc-comment encodes a test obligation the file ignores.

### 4.5 Is the `OverUnder_positive` contradiction fixed?
**No.** Still present at `BudgetCalculatorTests.cs:128-134`.

### 4.6 EstimatedSpendWithBuffer / Left status?
**Still zero tests.**

### 4.7 Is there an interface for `ProfileSession` / `ProfileSessionManager` to mock against?
**No.** Both are `sealed class`. Zero hits for `IProfileSessionManager` or `ISession`. **Single biggest impediment to any VM test landing.**

## 5. Top 10 tests to write at start of Phase 2

1. **P0** — `ApplyWageMask` truth-table coverage (4 assertions). Locks down privacy contract.
2. **P0** — `AddSpendAsync_StoresAmountAsNegative`. Guards sign convention.
3. **P0** — Rewrite misleading `OverUnder_positive_when_on_track_to_overshoot` + add genuine positive-case test.
4. **P1** — `EstimatedSpendWithBuffer_CombinesBurnRateAndBuffer` + `EstimatedLeftAfterBuffer_SubtractsFromGrandTotal`.
5. **P1** — `RepositoryTenantLeakTests` rewrite (reflection-driven, `NotBeEmpty` + `OnlyContain`).
6. **P1** — `TouchLastOpenedAsync_UpdatesTimestampFromClock` (with `FakeTimeProvider`).
7. **P1** — `TenantStampInterceptor_InsertWithMismatchingProfileId_Throws`.
8. **P2** — `ApplyRolloverFromPrior_NoPriorMonth_Throws`.
9. **P2** — `CurrentMonthViewModel_LoadAsync_WhenNoMonth_SetsNeedsMonthCreated`.
10. **P2** — `SeedCategorySeeder_Idempotent`.

## 6. One test-infrastructure investment

### `FakeProfileSessionManager` — test double for the DI scope hierarchy

**Problem.** `ProfileSessionManager` is `sealed class`; `ProfileSession` is `sealed class`; both expose concrete types. VM tests must boot full WPF host + real SQLite DB, which is why `App.Tests` is empty.

**Investment.** Two pieces:

1. **Extract `IProfileSessionManager`** in production code (one-line change):
```csharp
// src/PiggyBank.App/Profiles/IProfileSessionManager.cs
public interface IProfileSessionManager : IDisposable
{
    ProfileSession? Current { get; }
    void OpenProfile(Guid profileId);
    ProfileSession OpenAdminScope();
    Task EnsureInitialisedAsync(CancellationToken ct = default);
}
```
Update VM ctor sites (`CurrentMonthViewModel.cs:17-18`, `SettingsViewModel.cs:13`) to depend on the interface.

2. **`FakeProfileSessionManager`** in `tests/PiggyBank.App.Tests/Infrastructure/` — owns a `:memory:` SQLite DB, real DI container scoped per test. Tests against **real migrations and real interceptors** — VM tests double as E2E tenant-isolation gates.

**Estimated effort.** Half a day; pays back on first two VM tests.

## Files consulted
- `tests/PiggyBank.Data.Tests/Tenancy/{TenantLeakTests,RepositoryTenantLeakTests,TenantStampInterceptorTests,GlobalQueryFilterTests,TestDbFactory}.cs`
- `tests/PiggyBank.Data.Tests/Services/MonthServiceTests.cs`
- `tests/PiggyBank.Data.Tests/Profiles/ProfileAdminServiceTests.cs`
- `tests/PiggyBank.Core.Tests/Budgeting/BudgetCalculatorTests.cs`
- `src/PiggyBank.App/Dashboard/CurrentMonthViewModel.cs`
- `src/PiggyBank.App/Settings/SettingsViewModel.cs`
- `src/PiggyBank.Data/Services/MonthService.cs`
- `src/PiggyBank.Data/Profiles/ProfileAdminService.cs`
- `src/PiggyBank.Data/Tenancy/{TenantStampInterceptor,MutableTenantContext,ProfileSession}.cs`
- `src/PiggyBank.App/Profiles/ProfileSessionManager.cs`
- `src/PiggyBank.Data/AppDbContext.cs`
- `src/PiggyBank.Data/Seeding/SeedCategorySeeder.cs`
- All migrations and `ProfileOwnedEntity` subclasses
