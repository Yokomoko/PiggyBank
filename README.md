# PiggyBank

A local-first Windows desktop app for personal finance: payday-cycle budgeting, savings pockets, debt tracking with snowball/avalanche payoff simulation, side-income logging with UK tax-year set-aside estimates, and a joint-account view for households. Successor to a long-running Excel macro workbook, rebuilt as native WPF on .NET 10.

## Highlights

**Current month** · Single-screen dashboard for the active payday window.
- Salary, recurring outgoings, ledger transactions, with a running balance and per-day headroom
- "Allocation" outgoings (e.g. £150 for fuel) that ledger transactions automatically draw down from; overspend raises the month's effective bill without mutating the recurring template
- Salary-mask toggle for shoulder-surf privacy

**Pockets** · Named savings jars with auto-save splits.
- Per-pocket auto-save percentage applied to deposits; goal-reached pockets auto-pause so the rest of the percentage flows elsewhere
- Optional target dates with "£X/mo needed to hit goal" projections
- Direct deposit per pocket bypasses auto-save distribution; archive-with-transfer dialog moves the balance into another pocket before closing

**Debts** · Track balances, snapshot history.
- Grouped by kind (Credit card / Loan / Finance / Mortgage / Other) with humanised labels
- Inline-editable balance, limit, APR, minimum payment, scheduled overpayment, and overpayment-penalty days
- Payoff simulator runs three scenarios side-by-side: minimums-only baseline vs Avalanche (highest APR first) vs Snowball (smallest balance first), with rolling freed payments and 60-day overpayment interest penalties (typical UK loan terms)

**Side income** · Cash-in-hand work, freelance gigs, ad-hoc bonuses.
- Optional duration × hourly rate auto-fills the total; templates for recurring jobs
- Calendar-month grouping (independent of payday windows)
- Allocate any portion to a savings pocket OR as income on the main ledger (resolves the payday month containing `PaidOn`); per-row partials with remaining tracked
- "Email" button opens your default mail client with a pre-composed body; "Copy hours" puts a styled HTML table on the clipboard for paste-into-email
- UK tax-year (6 April to 5 April) set-aside estimates: Trading Allowance / Basic / Higher / Additional / Custom; picker persists per profile

**Joint accounts** · Shared-bank dashboard for households.
- Multiple accounts (bills, mortgage, etc.); per-account contributions and outgoings live outside profile tenancy so both partners see the same numbers
- Surplus / shortfall headline with "top up needed: £X" when contributions don't cover the bills

**Analytics** · Last-N-month charts + history.
- Monthly spend trend + spend-by-category (top 10), 3 / 6 / 12 month range
- Read-only past-month view loaded from the Months list (review history without altering it)
- CSV and PDF export per month

## Install

Grab the latest `setup.exe` from [Releases](https://github.com/Yokomoko/PiggyBank/releases) and run it. Installs to `%LocalAppData%\PiggyBank\` and needs no admin rights. Velopack handles delta auto-updates from the same source.

Data lives at `%LocalAppData%\PiggyBankData\app.db` (SQLite). Back it up by copying the `PiggyBankData\` folder; restore by replacing it. The folder is deliberately a sibling of the install dir, not a child, so an upgrade can never touch it.

## Build from source

```
dotnet restore PiggyBank.slnx
dotnet test PiggyBank.slnx --filter "FullyQualifiedName!~EndToEnd"
dotnet run --project src/PiggyBank.App
```

The `EndToEndTests` filter is exclusive: those use FlaUI to drive the live WPF window and aren't suitable for headless CI without a display.

For installer builds, see [`BUILD.md`](BUILD.md).

## Stack

- .NET 10, WPF, [WPF-UI](https://wpfui.lepo.co) (Fluent design), CommunityToolkit.Mvvm
- EF Core 10 + SQLite (code-first migrations; portable to other providers)
- [LiveCharts2](https://livecharts.dev/) for analytics
- [QuestPDF](https://www.questpdf.com/) for monthly PDF reports
- [Velopack](https://velopack.io/) for installer + auto-update
- xUnit, FluentAssertions, NSubstitute, FlaUI

## Architecture

Multi-tenancy lives at the data layer: every profile-owned entity inherits `ProfileOwnedEntity`, an EF Core global query filter scopes reads to `CurrentProfileId`, and a `SaveChanges` interceptor stamps writes. A reflection-driven `TenantLeakTests` enumerates every `ProfileOwnedEntity` and asserts no row from profile B leaks into profile A. Non-negotiable CI gate.

Joint-account data deliberately lives *outside* the tenant filter: it's shared by design, accessed via `ProfileSession.AdminScope()`.

```
src/
  PiggyBank.Core    Pure domain + BudgetCalculator + AllocationMath + TaxYearMath (no IO)
  PiggyBank.Data    EF Core + SQLite + tenancy interceptor + repositories
  PiggyBank.Import  xlsm parser + future bank CSV import
  PiggyBank.App     WPF shell (the only net10.0-windows project)

tests/
  PiggyBank.Core.Tests
  PiggyBank.Data.Tests        (includes TenantLeakTests, the non-negotiable CI gate)
  PiggyBank.Import.Tests
  PiggyBank.App.Tests
  PiggyBank.App.EndToEndTests (FlaUI; manual / display-attached runs only)
```

## Releasing

Push a `v*` tag (e.g. `v0.1.2`):

```
git tag v0.1.2
git push origin v0.1.2
```

GitHub Actions (`release.yml`) builds, packs with Velopack, and publishes a GitHub Release with `setup.exe` and delta-update metadata. Existing installs auto-update from the same source.

## License

Not yet licensed. Treat as "all rights reserved" until a `LICENSE` file lands. (TODO: pick MIT, Apache-2.0, or similar before flipping the repo to public.)
