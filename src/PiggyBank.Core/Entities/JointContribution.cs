namespace PiggyBank.Core.Entities;

/// <summary>
/// One profile's monthly contribution to a <see cref="JointAccount"/>.
/// <see cref="ProfileId"/> is a plain FK (NOT a query-filter trigger):
/// joint data is shared across both profiles and lives outside the
/// tenant filter. The FK exists only so the row can be rendered with
/// the contributor's display name (e.g. "Profile A: £500").
/// </summary>
/// <remarks>
/// Cascade-delete from <see cref="Profile"/> is intentionally
/// <c>Restrict</c>: archiving a profile must not silently drop its
/// joint contribution rows (would silently change the household total).
/// Cascade-delete from <see cref="JointAccount"/> IS active —
/// deleting an account naturally clears its contributions.
/// </remarks>
public sealed class JointContribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid JointAccountId { get; set; }
    public required Guid ProfileId { get; set; }
    public required decimal MonthlyAmount { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
}
