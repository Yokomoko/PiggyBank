using PiggyBank.Data.Tenancy;

namespace PiggyBank.App.Profiles;

/// <summary>
/// Contract for the single object that owns the active profile session.
/// Extracted so ViewModels can depend on an abstraction and be unit-tested
/// against a fake session manager (no real DI container or SQLite required).
/// </summary>
public interface IProfileSessionManager : IDisposable
{
    /// <summary>Active profile session, or null if no profile is open yet.</summary>
    ProfileSession? Current { get; }

    /// <summary>Runs one-time startup: DB migrations + seed catalog.
    /// Safe to call more than once — later calls are no-ops.</summary>
    Task EnsureInitialisedAsync(CancellationToken ct = default);

    /// <summary>Opens a profile session. Disposes the previous session
    /// (if any) first, so the old <see cref="AppDbContext"/> and its
    /// ChangeTracker can't leak state into the new profile's scope.</summary>
    void OpenProfile(Guid profileId);

    /// <summary>Transient admin-scoped session for Profile CRUD and bootstrap.
    /// Caller owns disposal.</summary>
    ProfileSession OpenAdminScope();
}
