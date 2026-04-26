namespace PiggyBank.Core.Entities;

/// <summary>
/// A monthly outgoing paid from a <see cref="JointAccount"/>. Stored
/// negative to match the <see cref="MonthlyOutgoing"/> convention so
/// totals can be summed without sign-flipping.
/// </summary>
public sealed class JointOutgoing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid JointAccountId { get; set; }
    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}
