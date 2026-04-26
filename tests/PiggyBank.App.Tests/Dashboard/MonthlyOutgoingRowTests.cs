using FluentAssertions;
using PiggyBank.App.Dashboard;
using PiggyBank.Core.Entities;

namespace PiggyBank.App.Tests.Dashboard;

/// <summary>
/// Privacy-critical: the 4-state truth table for <see cref="MonthlyOutgoingRow.ApplyWageMask"/>.
/// Any regression flipping the mask logic would expose wage amounts in
/// the outgoings grid. Phase 1 review flagged this as P0.
/// </summary>
public sealed class MonthlyOutgoingRowTests
{
    [Fact]
    public void Wage_row_masked_when_wage_hidden()
    {
        var row = MakeRow(amount: 3000m, isWage: true);
        row.ApplyWageMask(wageVisible: false);

        row.IsMasked.Should().BeTrue();
        row.DisplayAmount.Should().Be("••••");
    }

    [Fact]
    public void Wage_row_shows_amount_when_wage_visible()
    {
        var row = MakeRow(amount: 3000m, isWage: true);
        row.ApplyWageMask(wageVisible: true);

        row.IsMasked.Should().BeFalse();
        row.DisplayAmount.Should().Contain("3,000");
    }

    [Fact]
    public void Non_wage_row_never_masked_regardless_of_toggle()
    {
        var row = MakeRow(amount: -500m, isWage: false);

        row.ApplyWageMask(wageVisible: false);
        row.IsMasked.Should().BeFalse();
        row.DisplayAmount.Should().Contain("500");

        row.ApplyWageMask(wageVisible: true);
        row.IsMasked.Should().BeFalse();
        row.DisplayAmount.Should().Contain("500");
    }

    [Fact]
    public void Amount_changed_preserves_masking_state()
    {
        var row = MakeRow(amount: 3000m, isWage: true);
        row.ApplyWageMask(wageVisible: false);
        row.IsMasked.Should().BeTrue();

        row.Amount = 3500m;  // OnAmountChanged re-applies the mask

        row.IsMasked.Should().BeTrue("editing the wage amount must not unmask it");
        row.DisplayAmount.Should().Be("••••");
    }

    private static MonthlyOutgoingRow MakeRow(decimal amount, bool isWage) =>
        new(new MonthlyOutgoing
        {
            Name = isWage ? "Wage" : "Rent",
            Amount = amount,
            IsWage = isWage,
            MonthId = Guid.NewGuid(),
        });
}
