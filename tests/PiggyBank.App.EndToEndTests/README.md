# E2E tests (FlaUI)

End-to-end smoke tests that drive the real `PiggyBank.App` WPF process via UI Automation.

## Requirements

- **Windows with an interactive desktop session.** FlaUI talks to the real UIA tree; headless / SSH / nested RDP without input session all fail with a timeout. `windows-latest` on GitHub Actions is fine; Docker / WSL is not.
- The solution must be built in `Debug` first — the harness resolves `src/PiggyBank.App/bin/Debug/net10.0-windows/PiggyBank.App.exe` from disk.

## Running locally

```pwsh
dotnet build
dotnet test tests/PiggyBank.App.EndToEndTests
```

Each test launches a fresh app process into an isolated temp data dir (via the `PiggyBank_DATA_ROOT` env var), so nothing leaks between runs.

## Running in CI

The E2E tests are tagged `[Trait("Category", "E2E")]` so they're excluded from the default fast test run and gated into a separate workflow job that only runs on `windows-latest`:

```yaml
- name: E2E smoke
  run: dotnet test tests/PiggyBank.App.EndToEndTests --filter Category=E2E
```

## What's covered

- First-run create-profile wizard → MainWindow appears with profile heading.
- Empty-state "Start this month" → allowance panel renders.
- Add outgoing row → outgoings grid grows.
- Quick-add spend → ledger row appears.
- Close month → closed banner shows.
- Settings button opens Settings window.

## Adding a new E2E scenario

1. Add an `AutomationProperties.AutomationId` to any new interactive element you'll touch.
2. Use `AppHarness.Launch()` + `harness.WaitForWindow(...)` + `AppHarness.WaitForElement(...)` helpers.
3. Tag the class `[Trait("Category", "E2E")]`.

## Debugging a flaky failure

The harness throws a detailed `TimeoutException` that includes:

- Whether the app process exited (and its exit code)
- All top-level windows seen at the time of timeout (title + AutomationId)

If a window with the expected AutomationId is listed but `WaitForWindow` didn't match it, the automation tree isn't being queried correctly — inspect the element with [Accessibility Insights for Windows](https://accessibilityinsights.io/en/docs/windows/overview/).

If the process exited, check the app's crash logs at `%LocalAppData%\PiggyBank\Logs\` (production path) or the temp data-root from the test harness (will be cleaned up on dispose — add a breakpoint to inspect).
