namespace PiggyBank.Core.Entities;

/// <summary>
/// A template for a bill or recurring income line (mortgage, Spotify,
/// wage, etc.). Each month, the rollover process SNAPSHOTS these into
/// <see cref="MonthlyOutgoing"/> rows so editing the template never
/// retroactively changes historical months.
/// </summary>
public sealed class RecurringOutgoing : ProfileOwnedEntity
{
    public required string Name { get; set; }
    public decimal DefaultAmount { get; set; }     // signed: outgoings negative, income positive
    public Guid? CategoryId { get; set; }
    public bool IsIncome { get; set; }
    public bool IsWage { get; set; }                // special-case: privacy-masked, excluded from analytics by default
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }
}
