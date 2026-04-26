using PiggyBank.Core.Tenancy;

namespace PiggyBank.Data.Tenancy;

/// <summary>
/// Scoped implementation of <see cref="ITenantContext"/>. Set once at
/// scope creation by <c>ProfileSession</c>; immutable for the scope
/// lifetime. Switching profiles disposes the scope, which disposes the
/// <c>DbContext</c> — no state leaks.
/// </summary>
public sealed class MutableTenantContext : ITenantContext
{
    private Guid? _currentProfileId;
    private bool _isAdminScope;
    private bool _locked;

    public Guid? CurrentProfileId => _currentProfileId;
    public bool IsAdminScope => _isAdminScope;

    public void Set(Guid profileId)
    {
        if (_locked)
            throw new InvalidOperationException("TenantContext is immutable once set for the scope.");
        _currentProfileId = profileId;
        _isAdminScope = false;
        _locked = true;
    }

    public void SetAdminScope()
    {
        if (_locked)
            throw new InvalidOperationException("TenantContext is immutable once set for the scope.");
        _currentProfileId = null;
        _isAdminScope = true;
        _locked = true;
    }
}
