using FlaUI.Core.AutomationElements;
using FluentAssertions;

namespace PiggyBank.App.EndToEndTests;

[Trait("Category", "E2E")]
public sealed class CurrentMonthTests
{
    [Fact]
    public void Adding_an_outgoing_updates_the_outgoings_grid()
    {
        using var harness = AppHarness.Launch();
        FirstRunTests.CreateProfile(harness, "Alex");

        var main = harness.WaitForWindow("MainWindow");
        AppHarness.ClickButton(main, "StartMonthButton");

        AppHarness.SetText(main, "AddOutgoingNameBox", "Mortgage");
        AppHarness.SetText(main, "AddOutgoingAmountBox", "-1000");
        AppHarness.ClickButton(main, "AddOutgoingButton");

        var grid = AppHarness.WaitForElement(main, "OutgoingsGrid").AsDataGridView();
        grid.Rows.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Quick_add_spend_creates_a_ledger_row()
    {
        using var harness = AppHarness.Launch();
        FirstRunTests.CreateProfile(harness, "Alex");

        var main = harness.WaitForWindow("MainWindow");
        AppHarness.ClickButton(main, "StartMonthButton");

        AppHarness.SetText(main, "QuickAddPayeeBox", "Tesco");
        AppHarness.SetText(main, "QuickAddAmountBox", "20");
        AppHarness.ClickButton(main, "AddSpendButton");

        var ledger = AppHarness.WaitForElement(main, "LedgerGrid").AsDataGridView();
        ledger.Rows.Length.Should().Be(1);
    }

    [Fact]
    public void Close_month_shows_the_closed_banner()
    {
        using var harness = AppHarness.Launch();
        FirstRunTests.CreateProfile(harness, "Alex");

        var main = harness.WaitForWindow("MainWindow");
        AppHarness.ClickButton(main, "StartMonthButton");

        // Wait for the month to actually exist before trying to close it —
        // AllowedSpendPerDayText is only rendered once Month is not null, so
        // seeing it proves CreateCurrentMonthAsync has completed.
        AppHarness.WaitForElement(main, "AllowedSpendPerDayText");

        AppHarness.ClickButton(main, "CloseMonthButton");

        var banner = AppHarness.WaitForElement(main, "MonthClosedBanner");
        banner.IsOffscreen.Should().BeFalse("the closed banner should be rendered and visible");
    }

    [Fact]
    public void Settings_window_opens_from_main_chrome()
    {
        using var harness = AppHarness.Launch();
        FirstRunTests.CreateProfile(harness, "Alex");

        var main = harness.WaitForWindow("MainWindow");
        AppHarness.ClickButton(main, "SettingsButton");

        var settings = harness.WaitForWindow("SettingsWindow");
        AppHarness.WaitForElement(settings, "ThemeComboBox").Should().NotBeNull();
    }
}
