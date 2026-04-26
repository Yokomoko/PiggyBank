# Week 1+2 Audit Synthesis

Date: 2026-04-22. Source reviews:
- `arch-review.md` — architecture + multi-tenancy (senior architect agent)
- `code-review.md` — code quality + smells (code reviewer agent)
- `test-review.md` — test validity (test validator agent)
- This doc — feature-gap audit + prioritised fix list

## One-line verdict

**Foundations strong. Two real runtime bugs and two safety-test blind spots need fixing before Week 3. ~60% of Phase 1 MVP features are still unbuilt — the 6-week schedule was right but we're early-mid-sprint.**

---

## Strengths (from all three reviews, unanimous)

1. **Portable-core rule honoured to the letter.** `Core.csproj` has zero package or project references; grep confirms no WPF / EF / INotifyPropertyChanged leak.
2. **Query-filter cache gotcha understood and neutralised** with the `this.CurrentProfileId` pattern + explicit doc comment.
3. **Manual-rollover decision locked into tests.** `Create_does_not_auto_rollover_even_from_closed_prior` codifies the rule.
4. **`BudgetCalculator` is genuinely pure.** 32 tests cover edges. Payday calculator adds 12 more.
5. **DI scope hygiene.** `ProfileSession` owns the scope, switching disposes cleanly; `AppDbContext` / `MutableTenantContext` / `TenantStampInterceptor` all scoped correctly.

---

## 🔴 Must-fix before Week 3 (8 items, ~3 hours work)

| # | Finding | Source | Estimate |
|---|---|---|---|
| 1 | Missing FKs on `Month`, `MonthlyOutgoing`, `RecurringOutgoing`, `Transaction` — only `Category` and `ProfileSettings` have FK to `Profiles`. Phase-3 hard-delete will leave zombies. | arch §3.1 | 20 min + 1 migration |
| 2 | `DateTime.UtcNow` in `Profile.cs:16` — violates portable-core rule §2.0 | arch §3.2 | 10 min (inject `TimeProvider` at call site) |
| 3 | `MainWindow.ProfileColour` bound in XAML but never set → ring renders transparent | code #1 | 10 min |
| 4 | `ProfilePickerWindow.xaml:35` binds string hex to `Border.Background` without a converter → runtime binding error | code #2 | 15 min (add `HexToBrushConverter`) |
| 5 | No global exception handler; `App.OnStartup` is `async void` — startup failures crash silently | code #3 | 30 min |
| 6 | `CurrentMonthView` Ledger `CanUserAddRows="True"` but no persist wiring → user rows vanish | code #4 | 15 min (remove flag for now; Week 3 adds real quick-add) |
| 7 | `TenantLeakTests` passes vacuously for 5 of 7 DbSets (seeds only `Category` + `ProfileSettings`). Future `ProfileOwnedEntity` types silently escape coverage. | test §3.1 | 45 min (reflection-driven seeding + count assertion) |
| 8 | `TestDbFactory` uses `EnsureCreated()` — migrations themselves untested. | test §5.3 | 20 min (switch to `MigrateAsync`) |

---

## 🟡 Should-fix during Week 3 (hygiene — don't ship without these)

| # | Finding | Source |
|---|---|---|
| 9 | `IsWageVisible` / `ToggleWageCommand` / "Show wage" button are a half-feature — flag toggled but nothing redacts. Finish or delete. | code #5 |
| 10 | `CurrentMonthViewModel.CreateCurrentMonthAsync` resolves `AppDbContext` directly from DI — bypasses repository pattern. Introduce `IProfileSettingsRepository`. | code #6, arch §3.3 |
| 11 | `BudgetInputs.BufferPerDay` hardcoded to `10m` in VM — should read from `ProfileSettings.BufferPerDay`. | code #10 |
| 12 | `MonthService.CreateAsync` snapshot loses `IsIncome` (`MonthlyOutgoing` doesn't have the field) — wage and bonuses not distinguishable. Propagate. | code #8 |
| 13 | `ProfileAdminService.CreateAsync` — if category copy fails after Profiles insert, transaction rolls back but `_categories` state in Change Tracker is dirty. Verify. | code #12 |
| 14 | `RepositoryTenantLeakTests` uses `OnlyContain` without `NotEmpty` guard → passes vacuously. Also hardcodes 5 repos, not reflection-driven. | test §3.2 |
| 15 | `TenantStampInterceptor` branch coverage: insert-with-wrong-ProfileId throws, modify-without-changing-ProfileId succeeds, delete-succeeds. | test §3.4 |
| 16 | `BudgetCalculatorTests.Calc = new(TimeProvider.System)` — use `FakeTimeProvider` now, before any method actually reads `_clock`. | test §5.1 |
| 17 | `OverUnder_positive_when_on_track_to_overshoot` asserts `-300m` — name contradicts assertion. Rename or add the positive case. | test missing-edge #7 |
| 18 | `EstimatedSpendWithBuffer` + `EstimatedLeftAfterBuffer` have **zero tests**. | test missing-edge #9 |
| 19 | `SeedCategorySeeder` has **zero direct tests** — idempotency unverified. | test missing-edge #33 |
| 20 | `ProfileAdminService.TouchLastOpenedAsync` has **zero tests**. | test missing-edge #29 |

---

## 🟢 Defer to Phase 2+ / nice-to-have

- Extract `OnModelCreating` into `IEntityTypeConfiguration<T>` classes (code refactor)
- Introduce `ShellViewModel` for `MainWindow` (code refactor)
- `ProfileSessionManager` thread safety (not reachable until Switch Profile lands Week 4–5)
- Remove unused Serilog / LiveCharts NuGet refs if the Week-3 work doesn't need them
- Remaining 25+ missing edge cases from test review (low-value)

---

## Feature-gap audit — Phase 1 MVP vs shipped

From `02-product-roadmap.md` §MVP – Phase 1:

### ✅ Built

- Multi-tenancy safety rails (§2.1b of spec)
- Profile picker + create-profile wizard (with payday step)
- Domain model (`Profile`, `Category`, `RecurringOutgoing`, `Month`, `MonthlyOutgoing`, `Transaction`, `ProfileSettings`)
- 32-category seed catalogue + seeder
- Repository pattern (5 repos)
- `MonthService` with manual rollover + snapshotting
- `BudgetCalculator` — all 14 pure functions
- `PaydayCalculator` — UK rules + bank holidays
- `CurrentMonthView` skeleton (three-column layout, allowance panel, rollover banner)
- 65 tests green; CI tenant-leak gate

### ⚠️ Partially built (skeleton only, not end-to-end wired)

- **Current Month View** — reads outgoings + ledger + rollups, but outgoings are read-only and ledger can't add/delete. Allowance updates only on load, not on row edit.
- **Wage visibility toggle** — button + flag exist, no redaction behaviour.
- **Rollover banner** — suggests + applies from prior month, but no manual-edit field for `CarriedOverBalance` itself.

### ❌ Not started (Phase 1 MVP)

- **Dashboard as a separate screen** — currently Current Month serves both. Spec wants Dashboard with hero allowance + progress bar + top-5 category bar-chart + quick-add FAB.
- **Quick-add spend FAB** (acceptance criterion: creates transaction and recalculates in <1s).
- **Top-5 categories bar chart** (Dashboard).
- **Ledger add/edit/delete commands** — with category dropdown + autocomplete.
- **Outgoings add/edit/delete commands**.
- **Payday dates display + edit on Current Month screen**.
- **xlsm import wizard** — the biggest Phase 1 chunk (Week 3 focus).
- **Settings screen** — theme toggle, data-path display, wage privacy config.
- **Close-month UI ceremony** (service exists, no UI).
- **Golden-master test against the real `Money Manager.xlsm`** (acceptance criterion: calculations match Excel to 2 d.p. across 5 spot-check months).

---

## Recommended Week 3 order

The must-fixes are small enough to knock out in a single morning. Suggest:

### Morning session (~3 hours)

1. FKs + migration (#1)
2. `DateTime.UtcNow` → `TimeProvider` (#2)
3. ProfileColour wiring + HexToBrushConverter (#3, #4)
4. Global exception handler + `OnStartup` fix (#5)
5. Remove `CanUserAddRows="True"` from ledger until quick-add lands (#6)
6. Reflection-driven `TenantLeakTests` seeding (#7)
7. `MigrateAsync` in `TestDbFactory` (#8)
8. Quick test sweep (run all, should still be 65+).

### Afternoon session — Week 3 feature work

Pick one of:
- **Option A — quick-add spend + ledger/outgoings CRUD.** Most visible progress; closes the feature gap that prevents the app being useful.
- **Option B — xlsm import wizard.** Biggest outstanding item; unlocks the golden-master test against 11 years of real data.
- **Option C — Dashboard separation + top-5 chart.** Makes the app feel like a product.

**My recommendation:** Option A. The import wizard is big and complex; if we ship it first we can't actually use the app to validate it. If we ship CRUD first, you can enter a month's real data manually, feel the quick-add flow, and know whether the allowance engine rings true before we pour effort into importing 45 sheets' worth of history. Import becomes Week 4.
