# Phase 1 Sign-off

Date: 2026-04-22. Synthesises three independent agent reviews (`arch-review-phase1.md`, `code-review-phase1.md`, `test-review-phase1.md`) plus E2E infrastructure work.

## Verdict

**Phase 1 MVP feature-complete and ready to use for live tracking.** All 8 must-fixes from the Week 2 audit are genuinely closed. Two new 🔴 bugs surfaced in review and have been fixed in this session. One medium-severity architectural smell (service-locator pattern growing in the VM) is triaged to Phase 2 as GitHub issue #2.

The big known gap is that **the ViewModel layer has zero unit tests** — the `IProfileSessionManager` interface required to unblock that lands as issue #2.

## What shipped

- **Data layer**: multi-tenant SQLite with 7 safety rails (query filter, interceptor, `ProfileSession`, CHECK constraints, FKs, `ProfileAdminBypassTests`, reflection-driven `TenantLeakTests`)
- **Core layer**: `BudgetCalculator` (14 pure methods, 32 tests), `PaydayCalculator` (UK bank-holiday shift rules, 12 tests)
- **Import layer**: scaffolded only (xlsm parser deferred to Phase 3)
- **App layer**:
  - Profile picker + create-profile wizard with payday config
  - Current Month screen with full CRUD on outgoings and ledger
  - Category dropdown per row, live allowance recalculation
  - Quick-add spend form + `Ctrl+N` shortcut
  - Wage redaction via masked `DisplayAmount`
  - Editable month header (paydays + manual carry-over)
  - Rollover banner (manual, user confirms — never auto)
  - Close-month ceremony with read-only banner
  - Settings window (theme + per-profile payday/buffer/food-budget)
  - Global exception handler with crash files to `%LocalAppData%\PiggyBank\Logs\`
- **Tests**: 65 green (44 Core + 21 Data); reflection-driven tenant-leak gate; real migrations exercised
- **E2E infrastructure**: FlaUI harness + 7 initial smoke tests, AutomationIds on 20+ UI elements, isolated temp data dir per test via env var, gated CI job

## Fixes applied during review cycle

### 🔴 Fixed in this session (post-review)

| Finding | Source | File:line |
|---|---|---|
| Duplicate `<DataGridCheckBoxColumn Header="Future">` in ledger | arch §new-1, code §N-1 | `CurrentMonthView.xaml:209-214` — one instance removed |
| Row `PropertyChanged` handlers never detached on `Clear()` | code §N-2 | `CurrentMonthViewModel.LoadAsync` + `Clear()` + delete commands — detach before Clear / Remove |
| `MonthlyOutgoingRow.OnAmountChanged` inverted logic | code §N-3 | `OnAmountChanged` — replaced `!IsMasked \|\| !IsWage` with `!IsMasked` (wage-visibility = opposite of current mask) |
| `App.xaml` hardcoded `Theme="Light"` | arch §new-3 | Added comment — theme now genuinely resolved from `AppSettings` at startup via `SettingsWindow.ApplyTheme` |

### 🟡 Triaged to Phase 2 (GitHub issues)

- **#1** — E2E suite needs a real Windows desktop run to verify; CI job is `continue-on-error: true` until proven.
- **#2** — Top 3 refactors: extract `IProfileSessionManager` (unblocks `App.Tests`), split `CurrentMonthViewModel` into 3 sub-VMs, introduce `IThemeService` + `ShellViewModel`.
- **#3** — Phase 2 feature roadmap: analytics dashboard, debt redesign, month compare, category budgets.

## Known-weak test coverage (tracked)

Specific gaps flagged by the test validator; lined up as Phase 2 Week 1 work:

1. `MonthlyOutgoingRow.ApplyWageMask` — privacy-critical, 4-state truth table, 0 tests today
2. `AddSpendAsync` sign-flip (`Amount = -Math.Abs(...)`)
3. Genuine `OverUnder_positive` case (existing test name lies — asserts negative)
4. `EstimatedSpendWithBuffer` + `EstimatedLeftAfterBuffer` — both untested
5. `TouchLastOpenedAsync` — zero tests
6. `RepositoryTenantLeakTests` still vacuous-risky (hardcoded 5 repos, no `NotEmpty` guards)
7. `TenantStampInterceptor` insert-with-wrong-ProfileId branch
8. `ApplyRolloverFromPrior` no-prior-month throw path
9. `SeedCategorySeeder` idempotency
10. SQLite FK PRAGMA enforcement in test DB

All captured in `test-review-phase1.md §5`.

## Decisions and trade-offs taken

- **Rollover stays manual** (user request 2026-04-22) — never auto-computes at `MonthService.CreateAsync` time; user confirms via banner. Test `Create_does_not_auto_rollover_even_from_closed_prior` pins this.
- **Wage privacy is shoulder-surfer-prevention, not encryption** (spec §2.1b) — `DisplayAmount` masks the UI; the `Amount` field in the model stays correct so allowance math works.
- **xlsm import deferred** to Phase 3+ (user: "not urgent, starting live from now"). Golden-master test against real xlsm deferred with it.
- **Service-locator pattern in VMs** is technical debt — triaged, not refactored mid-phase to avoid scope creep. #2 addresses it.
- **`TestDbFactory` uses `:memory:` SQLite** — FK PRAGMA enforcement not proven by any test. Documented; P1 fix for Phase 2.

## Repo snapshot

- https://github.com/Yokomoko/PiggyBank (private)
- 5 commits on `main`: initial scaffold → Week 1 → Week 2 → Week 3 → audit fixes + E2E infra
- 65 tests passing (unit + integration), 7 E2E smoke tests awaiting local run
- CI workflow: 3 jobs (tenant-leak-gate → build-and-test → e2e)

## Sign-off

Phase 1 ready for daily use. Phase 2 kick-off blocked only on:
- (a) user running the app once for a real smoke
- (b) issue #1 E2E suite going green on a local desktop run
- (c) issue #2 landing before heavy feature work (unblocks VM tests)
