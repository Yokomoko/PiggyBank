using FluentAssertions;
using PiggyBank.Core.Payday;

namespace PiggyBank.Core.Tests.Payday;

public sealed class PaydayCalculatorTests
{
    // --- ResolveForMonth ---

    [Fact]
    public void Resolves_weekday_without_adjustment()
    {
        // 24 April 2026 is a Friday — no shift.
        PaydayCalculator.ResolveForMonth(2026, 4, 24, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2026, 4, 24));
    }

    [Fact]
    public void Shifts_saturday_back_to_friday()
    {
        // 25 April 2026 is a Saturday — pay on Friday 24.
        PaydayCalculator.ResolveForMonth(2026, 4, 25, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2026, 4, 24));
    }

    [Fact]
    public void Shifts_sunday_back_to_friday()
    {
        // 25 Jan 2026 is a Sunday — pay on Friday 23.
        PaydayCalculator.ResolveForMonth(2026, 1, 25, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2026, 1, 23));
    }

    [Fact]
    public void Shifts_past_bank_holiday()
    {
        // 25 Dec 2026 = Christmas = bank holiday. 24 is Thursday → pay Thurs 24.
        PaydayCalculator.ResolveForMonth(2026, 12, 25, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2026, 12, 24));
    }

    [Fact]
    public void Shifts_past_bank_holiday_and_weekend_combo()
    {
        // 25 Dec 2027 = Saturday. 24 = Friday = Christmas Eve (not a holiday),
        // BUT Christmas Day substitute lands on Mon 27 — 24th is still a working day.
        // Nominal Sat 25 → back to Fri 24 (works; it's not a bank holiday itself).
        PaydayCalculator.ResolveForMonth(2027, 12, 25, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2027, 12, 24));
    }

    [Fact]
    public void Respects_adjust_false()
    {
        // 25 April 2026 = Saturday. With adjust OFF, return the Saturday.
        PaydayCalculator.ResolveForMonth(2026, 4, 25, adjustForWeekendsAndBankHolidays: false)
            .Should().Be(new DateOnly(2026, 4, 25));
    }

    [Fact]
    public void Clamps_to_end_of_month_when_day_exceeds()
    {
        // 31 February → clamp to 28 Feb 2026 (Saturday) → back to Friday 27.
        PaydayCalculator.ResolveForMonth(2026, 2, 31, adjustForWeekendsAndBankHolidays: true)
            .Should().Be(new DateOnly(2026, 2, 27));
    }

    [Fact]
    public void Rejects_invalid_day_of_month()
    {
        var act = () => PaydayCalculator.ResolveForMonth(2026, 4, 0, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- ResolvePayWindow ---

    [Fact]
    public void Window_when_today_before_this_months_payday()
    {
        // Today = 10 April 2026 (Friday). Nominal 24th = Friday 24 April.
        // Last payday = 24 March 2026 = Tuesday.
        var (last, next) = PaydayCalculator.ResolvePayWindow(
            new DateOnly(2026, 4, 10), dayOfMonth: 24, adjustForWeekendsAndBankHolidays: true);

        last.Should().Be(new DateOnly(2026, 3, 24));
        next.Should().Be(new DateOnly(2026, 4, 24));
    }

    [Fact]
    public void Window_when_today_equals_payday_is_current_month()
    {
        var (last, next) = PaydayCalculator.ResolvePayWindow(
            new DateOnly(2026, 4, 24), dayOfMonth: 24, adjustForWeekendsAndBankHolidays: true);

        last.Should().Be(new DateOnly(2026, 4, 24));
        next.Should().Be(new DateOnly(2026, 5, 22));   // 25 May 2026 is a Monday, but 25 is "Spring bank holiday" in our table → shift to Friday 22.
    }

    [Fact]
    public void Window_after_this_months_payday_rolls_into_next()
    {
        var (last, next) = PaydayCalculator.ResolvePayWindow(
            new DateOnly(2026, 4, 26), dayOfMonth: 24, adjustForWeekendsAndBankHolidays: true);

        last.Should().Be(new DateOnly(2026, 4, 24));
        next.Should().Be(new DateOnly(2026, 5, 22));
    }

    // --- UkBankHolidays sanity ---

    [Fact]
    public void BankHolidays_contains_known_dates()
    {
        UkBankHolidays.IsBankHoliday(new DateOnly(2026, 12, 25)).Should().BeTrue();
        UkBankHolidays.IsBankHoliday(new DateOnly(2026, 4, 3)).Should().BeTrue();   // Good Friday
        UkBankHolidays.IsBankHoliday(new DateOnly(2026, 4, 24)).Should().BeFalse(); // not a holiday
    }
}
