namespace PiggyBank.Core.Entities;

/// <summary>
/// A point-in-time observation of a debt's balance. Stored as negative
/// (£-500 means £500 owed) to match the workbook's sign convention and
/// make <c>SUM(Amount)</c> across debts + outgoings net correctly.
/// </summary>
/// <remarks>
/// In the workbook the "After" cell of each debt was a formula tied to
/// a specific outgoing row (<c>=M5 - B11</c>). That was brittle — we
/// persist the observed value here as a plain decimal and let the user
/// audit / edit it rather than recomputing.
/// </remarks>
public sealed class DebtSnapshot : ProfileOwnedEntity
{
    public required Guid DebtId { get; set; }

    /// <summary>When the snapshot was taken. Usually aligned to a month-end
    /// or statement date but not required to be.</summary>
    public required DateOnly SnapshotDate { get; set; }

    /// <summary>Balance in profile currency, negative = owed.</summary>
    public required decimal Balance { get; set; }

    public string? Notes { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
