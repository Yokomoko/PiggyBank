namespace PiggyBank.Core.Entities;

/// <summary>
/// A shared bank account that is jointly funded by more than one
/// <see cref="Profile"/>. Unlike <see cref="ProfileOwnedEntity"/> children,
/// joint accounts and their children are deliberately NOT tenant-filtered —
/// they're visible to every profile so each user can see the full
/// household picture without switching context.
/// </summary>
/// <remarks>
/// Two partners sharing a "joint bills" account is the canonical case:
/// both pay into it from their own salaries and the household has a single
/// list of outgoings (rent, council tax, utilities). The aggregate
/// contributions vs outgoings is what matters; per-profile share split
/// is intentionally NOT modelled because the app has no use for "who
/// owes whom" at this level.
/// </remarks>
public sealed class JointAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? BankName { get; set; }
    public string? Notes { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public int SortOrder { get; set; }
}
