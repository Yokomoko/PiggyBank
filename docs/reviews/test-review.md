# Test Suite Review — Week 2 Audit

Produced by `apex-dotnet-plugin:Test Validator` agent 2026-04-22. Agent's system prompt forbade writing to disk; this is its verbatim output captured by the parent session.

## Verdict

**Proves correctness of calculators; smoke-tests multi-tenancy with one genuinely false-positive and serious gaps in ownership-mutation paths.** `BudgetCalculator` suite is strong. Multi-tenancy gate (`TenantLeakTests`) has a subtle but real hole in the reflection-driven discovery. `MonthService` rollover logic is well-covered for happy paths but misses financial-arithmetic assertions.

## Test count

| Project | Tests | Verdict |
|---|---|---|
| `PiggyBank.Core.Tests` | 44 (32 BudgetCalculator + 12 Payday) | Reasonable for calculator, thin for payday |
| `PiggyBank.Data.Tests` | 21 | Under-covered for CRUD, missing seeder coverage |
| `PiggyBank.Import.Tests` | 0 | Acceptable now — flag if Week 3 lands imports |
| `PiggyBank.App.Tests` | 0 | Concerning — wage-masking + quick-add untested |

`BudgetCalculator` has 13 public methods. QA strategy §2 specifies 3–6 boundary cases per method ≈ 60 expected; actual is 32. **Roughly half the spec catalogue.**

## False-positive findings

### 3.1 `TenantLeakTests.No_DbSet_returns_rows_from_other_profile` — `tests/PiggyBank.Data.Tests/Tenancy/TenantLeakTests.cs:21`

**Strength:** Weak — looks strong, has a subtle hole.

Seeds only 2 of the 7 `ProfileOwnedEntity` DbSets (`Category` + `ProfileSettings`). For `RecurringOutgoings`, `Months`, `MonthlyOutgoings`, `Transactions` the assertion `foreach (var item in result) { ... }` iterates an empty collection and **passes vacuously**. A new `DbSet<Foo>` added by a future developer without wiring `ApplyProfileFilter` would be discovered by reflection, find zero rows, and the test would pass.

### 3.2 `RepositoryTenantLeakTests.Repositories_never_return_rows_from_other_profile` — `tests/PiggyBank.Data.Tests/Tenancy/RepositoryTenantLeakTests.cs:17`

`OnlyContain` passes vacuously on empty collections. Test pairs every `OnlyContain` without a `NotEmpty` guard. Also hardcodes five specific repos — a new repo would silently escape coverage.

### 3.3 `ProfileAdminServiceTests.Admin_service_refuses_to_run_from_profile_scope` — `tests/PiggyBank.Data.Tests/Profiles/ProfileAdminServiceTests.cs:71`

Hand-rolls a profile-scoped `MutableTenantContext` rather than reproducing real DI scope behaviour. Only proves `EnsureAdminScope()` throws when `IsAdminScope == false` — which is trivially true from the implementation. Covers only `ListAsync`; `CreateAsync`/`ArchiveAsync`/`FindAsync` untested.

### 3.4 `TenantStampInterceptorTests` — missing branch coverage

Covers: Insert-stamps-when-unset, Insert-without-scope-throws, Updating-profile-id-throws. **Missing:**
- Insert with ALREADY-SET ProfileId NOT matching scope (line 50-55 of interceptor)
- Insert with ALREADY-SET ProfileId MATCHING scope (should succeed silently)
- Modify that does NOT change ProfileId (should succeed)
- Delete of a ProfileOwnedEntity

## Missing edge cases (33 items)

### BudgetCalculator
1. `GrandTotal` — carry-over exceeds grand total
2. `GrandTotal_TypicalMonth` with QA-§2.1 workbook-realistic values
3. `AllowedSpendPerDay` — sub-day fraction (0.5 days)
4. `AllowedSpendWholeDays` — exactly-one-day and 0.5-ceils-to-1 edge
5. `SpentPerDayToDate` — negative DaysSincePayday
6. `EstimatedSpend` — zero days remaining
7. **`OverUnder` — positive (on-track-to-overshoot) case — test name contradicts its assertion** (line 129 named "positive_when_on_track_to_overshoot" but asserts -300)
8. `AllowedWeeklyRemaining` — 3-day pro rata
9. `EstimatedSpendWithBuffer` / `EstimatedLeftAfterBuffer` — **zero tests** for either method
10. `WeeklyFoodSpendSoFar` — week-boundary sign flip at 6.99 vs 7.01 days
11. `AllowedWeeklyFoodRemaining` — weeksUntilPayday zero guard
12. `DebtUtilisation` — mixed zero-limit and real limits (semantic judgement call)
13. `SavingsSnowballOneMonth` — negative contribution (withdrawal)

### PaydayCalculator
14. **Non-shift 25th-cycle** baseline (Feb 2027 25th = Thursday → same day)
15. Empty UK bank-holiday table (if catalogue becomes data-driven)
16. Year beyond `LatestYearCovered` (2030+)
17. Leap-year 29 Feb edge
18. True bank-holiday + weekend cascade (Dec 28 2026 → back to Dec 24)
19. `ResolvePayWindow` — year-boundary crossing (Jan 5 2027 → prior = Dec 24 2026)
20. `ResolveForMonth` with `dayOfMonth: 31` in a 31-day month
21. `ArgumentOutOfRangeException` for `dayOfMonth > 31`

### MonthService
22. `ComputeClosingBalanceAsync` — direct test with carry-in + mixed past/future transactions
23. Multi-month rollover chain (A closed, B open, C new — does C reach past B to A?)
24. `ApplyRolloverFromPriorAsync` — no prior month throws
25. `CreateAsync` — `carryOverOverride` + snapshotting interaction
26. `CreateAsync` — explicit NotContain("Old gym") for archived exclusion
27. Ordering/SortOrder of snapshotted outgoings

### ProfileAdminService
28. `ArchiveAsync` — profile not found throws
29. `TouchLastOpenedAsync` — **zero tests** (entire method untested)
30. `CreateAsync` — empty/whitespace displayName throws
31. `CreateAsync` — `ProfileSettings` row is actually created for the new profile

### AppDbContext filter
32. Filter active for every `ProfileOwnedEntity` type (only `Category` tested)

### SeedCategorySeeder
33. `SeedCategorySeeder` — **zero direct tests**: idempotency, SortOrder updates on re-run, case-insensitive name merging

## Determinism & isolation concerns

### 5.1 BudgetCalculatorTests uses `TimeProvider.System`

`BudgetCalculatorTests.cs:8` — violates QA strategy §1 "inject FakeTimeProvider" rule. Today works because `_clock` is unused in production, but the moment any calc method reads `_clock.GetLocalNow()` several tests will become flaky.

### 5.2 TestDb.SwitchToProfile leaves stale `Tenant` property

`TestDbFactory.cs:62` retains the original admin `Tenant`; any test reading `db.Tenant` after a switch gets the admin context. `ProfileAdminServiceTests.cs:82-84` works around this by building a fresh `MutableTenantContext` — which is the hand-rolled reproduction flagged in § 3.3. The helper encourages the flawed pattern.

### 5.3 `EnsureCreated()` vs `MigrateAsync()`

QA strategy §1 says "`MigrateAsync()` once per test-class fixture; `UseInMemoryDatabase` is forbidden." `TestDbFactory.cs:41` uses `EnsureCreated()` which skips migrations entirely — **no test verifies migrations apply cleanly to a blank DB.**

## Three particularly good tests

1. **`TenantStampInterceptorTests.Updating_profile_id_throws`** — realistic write path, proves interceptor survived one successful save before blocking the second.
2. **`GlobalQueryFilterTests` matched pair** — the IgnoreQueryFilters test proves the bypass works, preventing a false positive where over-aggressive filter returns nothing.
3. **`MonthServiceTests.Create_does_not_auto_rollover_even_from_closed_prior`** — nails down a business rule rather than implementation detail; `because` parameter self-documents intent.

## Top 5 tests to add before Week 3

1. **P1 (critical):** Strengthen `TenantLeakTests` with reflection-driven seeding so new DbSets can't silently pass.
2. **P2 (high):** Test the "insert with wrong ProfileId" branch on `TenantStampInterceptor`.
3. **P3 (high):** Direct test on `ComputeClosingBalanceAsync` with carry-in + future transactions.
4. **P4 (high):** `PaydayCalculator` — non-shift 25th-cycle case + leap-year 29-Feb clamp.
5. **P5 (medium):** `SeedCategorySeeder` idempotency test.
