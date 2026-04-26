using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.Data;
using PiggyBank.Data.Seeding;
using PiggyBank.Data.Tenancy;

namespace PiggyBank.App.Profiles;

/// <summary>
/// Owns the currently active <see cref="ProfileSession"/>. Ensures the DB
/// schema and seed-category catalog are up to date before any profile
/// session is opened. Switching profiles disposes the old session and
/// creates a new one from the root provider.
/// </summary>
public sealed class ProfileSessionManager(IServiceProvider rootProvider) : IProfileSessionManager
{
    private ProfileSession? _current;
    private bool _bootstrapped;

    public ProfileSession? Current => _current;

    public async Task EnsureInitialisedAsync(CancellationToken ct = default)
    {
        if (_bootstrapped) return;

        // Admin scope: apply migrations + seed the shared catalog.
        using var admin = ProfileSession.AdminScope(rootProvider);
        var db = admin.Services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        var seeder = admin.Services.GetRequiredService<SeedCategorySeeder>();
        await seeder.EnsureSeededAsync(ct);

        _bootstrapped = true;
    }

    public void OpenProfile(Guid profileId)
    {
        _current?.Dispose();
        _current = new ProfileSession(rootProvider, profileId);
    }

    public ProfileSession OpenAdminScope() => ProfileSession.AdminScope(rootProvider);

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
    }
}
