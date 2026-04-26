using FluentAssertions;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;

namespace PiggyBank.Core.Tests.Budgeting;

public class TaxYearMathTests
{
    [Theory]
    [InlineData("2026-04-05", 2025)]   // last day of 2025/26
    [InlineData("2026-04-06", 2026)]   // first day of 2026/27
    [InlineData("2026-12-31", 2026)]   // mid-year
    [InlineData("2027-04-05", 2026)]   // last day of 2026/27
    [InlineData("2027-04-06", 2027)]   // boundary
    [InlineData("2026-01-01", 2025)]   // before April → previous tax year
    public void TaxYearStartYearOf_handles_april_6_boundary(string date, int expected)
        => TaxYearMath.TaxYearStartYearOf(DateOnly.Parse(date)).Should().Be(expected);

    [Fact]
    public void Label_formats_as_yyyy_yy()
    {
        TaxYearMath.Label(2026).Should().Be("2026/27");
        TaxYearMath.Label(2099).Should().Be("2099/00");  // wraps; cosmetic only
    }

    [Fact]
    public void Estimate_TradingAllowance_below_1k_charges_nothing()
    {
        var s = TaxYearMath.Estimate(2026, 800m, TaxBand.TradingAllowance, null);
        s.SetAside.Should().Be(0m);
        s.EffectiveRate.Should().Be(0m);
        s.Note.Should().Contain("Within £1,000");
    }

    [Fact]
    public void Estimate_TradingAllowance_above_1k_charges_basic_on_excess()
    {
        // £1500 earned. £1000 allowance free. £500 taxable at 26%.
        var s = TaxYearMath.Estimate(2026, 1500m, TaxBand.TradingAllowance, null);
        s.SetAside.Should().Be(130m);  // 500 * 0.26
        s.EffectiveRate.Should().Be(0.26m);
    }

    [Fact]
    public void Estimate_Basic_charges_26pct_on_total()
        => TaxYearMath.Estimate(2026, 1000m, TaxBand.Basic, null).SetAside.Should().Be(260m);

    [Fact]
    public void Estimate_Higher_charges_42pct()
        => TaxYearMath.Estimate(2026, 1000m, TaxBand.Higher, null).SetAside.Should().Be(420m);

    [Fact]
    public void Estimate_Additional_charges_47pct()
        => TaxYearMath.Estimate(2026, 1000m, TaxBand.Additional, null).SetAside.Should().Be(470m);

    [Fact]
    public void Estimate_Custom_uses_supplied_rate()
        => TaxYearMath.Estimate(2026, 1000m, TaxBand.Custom, 0.30m).SetAside.Should().Be(300m);

    [Fact]
    public void Estimate_Custom_with_null_rate_yields_zero()
        => TaxYearMath.Estimate(2026, 1000m, TaxBand.Custom, null).SetAside.Should().Be(0m);

    [Fact]
    public void SummariseByTaxYear_groups_entries_by_tax_year_boundary()
    {
        var entries = new[]
        {
            Entry(new(2026, 3, 15), 100m),  // pre-Apr-6 → 2025/26
            Entry(new(2026, 4, 5), 50m),    // last day of 2025/26
            Entry(new(2026, 4, 6), 75m),    // first day of 2026/27
            Entry(new(2027, 3, 31), 200m),  // 2026/27
        };
        var summary = TaxYearMath.SummariseByTaxYear(entries, TaxBand.Basic, null);

        summary.Should().HaveCount(2);
        summary[0].StartYear.Should().Be(2026);  // newest first
        summary[0].TotalEarned.Should().Be(275m); // 75 + 200
        summary[1].StartYear.Should().Be(2025);
        summary[1].TotalEarned.Should().Be(150m); // 100 + 50
    }

    [Fact]
    public void SummariseByTaxYear_empty_input_returns_empty_list()
        => TaxYearMath.SummariseByTaxYear(Array.Empty<SideIncomeEntry>(), TaxBand.Basic, null)
            .Should().BeEmpty();

    private static SideIncomeEntry Entry(DateOnly paidOn, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = Guid.NewGuid(),
        PaidOn = paidOn,
        Total = total,
        CreatedAtUtc = DateTime.UtcNow,
    };
}
