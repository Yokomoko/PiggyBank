using FlaUI.Core.AutomationElements;
using FluentAssertions;

namespace PiggyBank.App.EndToEndTests;

/// <summary>
/// Smoke-level scenarios that exercise the create-profile wizard and
/// prove the empty-state Current Month screen renders.
/// </summary>
[Trait("Category", "E2E")]
public sealed class FirstRunTests
{
    [Fact]
    public void First_run_shows_create_profile_wizard_then_opens_main_window()
    {
        using var harness = AppHarness.Launch();
        harness.CleanupOnDispose = false;  // keep temp dir for post-mortem if this fails

        // On a fresh data dir the app skips the picker and lands on the
        // create-profile wizard directly.
        var wizard = harness.WaitForWindow("CreateProfileWindow");

        AppHarness.SetText(wizard, "DisplayNameBox", "Alex");
        // PaydayDayBox deliberately NOT touched — accepting its default of 25.
        // WPF-UI NumberBox is tough to drive reliably via UIA and no test
        // depends on a specific payday value for correctness checks.

        AppHarness.ClickButton(wizard, "ConfirmCreateProfileButton");

        // MainWindow appears with the profile heading text.
        var main = harness.WaitForWindow("MainWindow");
        var heading = AppHarness.WaitForElement(main, "ProfileHeadingText").AsLabel();
        heading.Text.Should().Contain("Alex");

        // Passed — allow cleanup.
        harness.CleanupOnDispose = true;
    }

    [Fact]
    public void Start_month_empty_state_creates_an_open_month()
    {
        using var harness = AppHarness.Launch();
        CreateProfile(harness, "Alex");

        var main = harness.WaitForWindow("MainWindow");
        AppHarness.ClickButton(main, "StartMonthButton");

        var allowed = AppHarness.WaitForElement(main, "AllowedSpendPerDayText").AsLabel();
        allowed.Text.Should().StartWith("£");
    }

    /// <summary>
    /// Minimal happy-path profile creation for tests that just need SOME
    /// profile to exist. Skips the payday NumberBox — we accept the default
    /// of 25 since no downstream test depends on the specific payday value.
    /// </summary>
    internal static void CreateProfile(AppHarness harness, string name)
    {
        var wizard = harness.WaitForWindow("CreateProfileWindow");
        AppHarness.SetText(wizard, "DisplayNameBox", name);
        AppHarness.ClickButton(wizard, "ConfirmCreateProfileButton");
        harness.WaitForWindow("MainWindow");
    }
}
