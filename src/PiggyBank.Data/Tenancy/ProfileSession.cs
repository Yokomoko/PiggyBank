using Microsoft.Extensions.DependencyInjection;

namespace PiggyBank.Data.Tenancy;

/// <summary>
/// Wraps a DI scope bound to a single profile. Disposing the session
/// disposes the scope, the <c>DbContext</c>, and the <c>ChangeTracker</c>,
/// which is what makes profile switching leak-safe.
/// </summary>
public sealed class ProfileSession : IDisposable
{
    private readonly IServiceScope _scope;
    private bool _disposed;

    public IServiceProvider Services => _scope.ServiceProvider;
    public Guid ProfileId { get; }

    public ProfileSession(IServiceProvider root, Guid profileId)
    {
        _scope = root.CreateScope();
        ProfileId = profileId;
        var tenant = _scope.ServiceProvider.GetRequiredService<MutableTenantContext>();
        tenant.Set(profileId);
    }

    public static ProfileSession AdminScope(IServiceProvider root)
    {
        var scope = root.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<MutableTenantContext>();
        tenant.SetAdminScope();
        return new ProfileSession(scope, Guid.Empty);
    }

    private ProfileSession(IServiceScope scope, Guid profileId)
    {
        _scope = scope;
        ProfileId = profileId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scope.Dispose();
    }
}
