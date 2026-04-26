using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PiggyBank.Core.Entities;
using PiggyBank.Core.Tenancy;

namespace PiggyBank.Data.Tenancy;

/// <summary>
/// Auto-stamps <see cref="ProfileOwnedEntity.ProfileId"/> on insert and
/// blocks any attempt to change it on update. Paired with the
/// DbContext's global query filter, this is the primary multi-tenancy
/// safety rail in application code.
/// </summary>
public sealed class TenantStampInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        foreach (EntityEntry<ProfileOwnedEntity> entry
                 in context.ChangeTracker.Entries<ProfileOwnedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.ProfileId == Guid.Empty)
                    {
                        entry.Entity.ProfileId = tenant.CurrentProfileId
                            ?? throw new InvalidOperationException(
                                $"No profile in scope while inserting {entry.Entity.GetType().Name}.");
                    }
                    else if (tenant.CurrentProfileId is { } cur && entry.Entity.ProfileId != cur)
                    {
                        throw new InvalidOperationException(
                            $"Attempted to insert {entry.Entity.GetType().Name} with ProfileId {entry.Entity.ProfileId} " +
                            $"but current scope is {cur}.");
                    }
                    break;

                case EntityState.Modified:
                    var original = (Guid)entry.OriginalValues[nameof(ProfileOwnedEntity.ProfileId)]!;
                    if (original != entry.Entity.ProfileId)
                    {
                        throw new InvalidOperationException(
                            $"{entry.Entity.GetType().Name}.ProfileId is immutable. " +
                            $"Attempted change from {original} to {entry.Entity.ProfileId}.");
                    }
                    break;
            }
        }
    }
}
