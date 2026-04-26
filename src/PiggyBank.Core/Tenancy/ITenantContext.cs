namespace PiggyBank.Core.Tenancy;

/// <summary>
/// Resolves the currently active profile for the scope. The Data layer's
/// global query filter and <c>SaveChanges</c> interceptor both read this
/// to keep every query and every insert bound to one profile.
/// </summary>
/// <remarks>
/// Registered as a scoped service. A new scope is created every time the
/// user switches profile, which disposes the prior <c>DbContext</c> and
/// <c>ChangeTracker</c>, preventing state from leaking between profiles.
/// </remarks>
public interface ITenantContext
{
    Guid? CurrentProfileId { get; }
    bool IsAdminScope { get; }
}
