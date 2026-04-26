using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;
using PiggyBank.Core.Tenancy;

namespace PiggyBank.Data.Profiles;

/// <summary>
/// The ONE legitimate cross-profile service. Lists, creates, archives
/// <see cref="Profile"/> rows. All queries use <c>IgnoreQueryFilters</c>
/// — this is the only class allowed to do so in application code
/// (enforced by analyser <c>MM0001</c> and CI grep).
///
/// Resolved only from the admin DI scope (<c>ProfileSession.AdminScope</c>).
/// The tenant interceptor is still wired, but since <see cref="Profile"/>
/// does not inherit <see cref="ProfileOwnedEntity"/>, it doesn't try to
/// stamp anything on insert.
/// </summary>
public sealed class ProfileAdminService(AppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private DateTime NowUtc() => clock.GetUtcNow().UtcDateTime;

    /// <summary>Returns all non-archived profiles ordered by last-opened.</summary>
    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct = default)
    {
        EnsureAdminScope();
        return await db.Profiles
            .IgnoreQueryFilters()
            .Where(p => p.ArchivedAtUtc == null)
            .OrderByDescending(p => p.LastOpenedAtUtc)
            .ThenBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<Profile?> FindAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdminScope();
        return await db.Profiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>Creates a profile and copies the selected seed categories into
    /// that profile's <see cref="Category"/> table. Runs in one transaction.</summary>
    public async Task<Profile> CreateAsync(
        string displayName,
        string colourHex,
        string iconKey,
        IEnumerable<int>? seedCategoryIds = null,
        ProfileSettingsInput? settings = null,
        CancellationToken ct = default)
    {
        EnsureAdminScope();
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName required.", nameof(displayName));

        var profile = new Profile
        {
            DisplayName = displayName.Trim(),
            ColourHex = colourHex,
            IconKey = iconKey,
            CreatedAtUtc = NowUtc(),
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(ct);

        // Copy seed categories into the profile.
        var selectedIds = (seedCategoryIds ?? []).ToHashSet();
        var seedsToCopy = selectedIds.Count > 0
            ? await db.SeedCategories.Where(s => selectedIds.Contains(s.Id)).ToListAsync(ct)
            : await db.SeedCategories.Where(s => s.DefaultEnabled).ToListAsync(ct);

        foreach (var seed in seedsToCopy)
        {
            db.Categories.Add(new Category
            {
                ProfileId = profile.Id,         // explicit here since we're in admin scope, no interceptor assistance
                Name = seed.Name,
                Kind = seed.Kind,
                SourceSeedCategoryId = seed.Id,
            });
        }

        // Default ProfileSettings row so the calculator never hits a null.
        var profileSettings = new ProfileSettings { ProfileId = profile.Id };
        if (settings is not null)
        {
            profileSettings.PrimaryPaydayDayOfMonth = settings.PrimaryPaydayDayOfMonth;
            profileSettings.AdjustPaydayForWeekendsAndBankHolidays = settings.AdjustPaydayForWeekendsAndBankHolidays;
        }
        db.ProfileSettings.Add(profileSettings);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return profile;
    }

    public async Task ArchiveAsync(Guid profileId, CancellationToken ct = default)
    {
        EnsureAdminScope();
        var profile = await db.Profiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == profileId, ct)
            ?? throw new InvalidOperationException($"Profile {profileId} not found.");

        profile.ArchivedAtUtc = NowUtc();
        await db.SaveChangesAsync(ct);
    }

    public async Task TouchLastOpenedAsync(Guid profileId, CancellationToken ct = default)
    {
        // Called from non-admin scope: a profile marking itself as opened.
        // Allowed because Profile is not a ProfileOwnedEntity.
        var profile = await db.Profiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == profileId, ct)
            ?? throw new InvalidOperationException($"Profile {profileId} not found.");

        profile.LastOpenedAtUtc = NowUtc();
        await db.SaveChangesAsync(ct);
    }

    private void EnsureAdminScope()
    {
        if (!tenant.IsAdminScope)
            throw new InvalidOperationException(
                "ProfileAdminService must be resolved from an admin DI scope (ProfileSession.AdminScope).");
    }
}

/// <summary>Inputs the user can set on a new profile's <see cref="ProfileSettings"/>.</summary>
public sealed record ProfileSettingsInput(
    int PrimaryPaydayDayOfMonth,
    bool AdjustPaydayForWeekendsAndBankHolidays);
