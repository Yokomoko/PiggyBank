namespace PiggyBank.Core.Entities;

/// <summary>
/// Base for every entity that belongs to a single <see cref="Profile"/>.
/// The <see cref="ProfileId"/> column is stamped automatically by
/// <c>TenantStampInterceptor</c> on insert and immutable thereafter.
/// Never set <see cref="ProfileId"/> manually in application code.
/// </summary>
public abstract class ProfileOwnedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
}
