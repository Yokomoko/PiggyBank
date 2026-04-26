namespace PiggyBank.Core.Entities;

/// <summary>
/// A single spend/income line in the ledger (columns D-G in the workbook).
/// Belongs to a <see cref="Month"/>; categorised via <see cref="Category"/>.
/// </summary>
public sealed class Transaction : ProfileOwnedEntity
{
    public required Guid MonthId { get; set; }
    public required DateOnly Date { get; set; }
    public required string Payee { get; set; }
    public required decimal Amount { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Notes { get; set; }

    // Import auditing — Phase 2+ will set these via PiggyBank.Import.
    // Null = manually entered by user.
    public string? ImportSource { get; set; }
    public Guid? ImportRunId { get; set; }
}
