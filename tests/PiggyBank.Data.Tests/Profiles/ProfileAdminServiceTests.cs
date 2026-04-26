using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Profiles;
using PiggyBank.Data.Seeding;
using PiggyBank.Data.Tenancy;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Profiles;

public sealed class ProfileAdminServiceTests
{
    [Fact]
    public async Task Create_with_default_seed_copies_default_enabled_categories()
    {
        using var db = TestDb.CreateAdmin();
        await new SeedCategorySeeder(db.Context).EnsureSeededAsync();

        var admin = new ProfileAdminService(db.Context, db.Tenant, TimeProvider.System);
        var profile = await admin.CreateAsync("Alex", "#3B82F6", "person");

        db.SwitchToProfile(profile.Id);
        var cats = await db.Context.Categories.ToListAsync();

        // every copied category should belong to the new profile and have a source seed id
        cats.Should().NotBeEmpty();
        cats.Should().OnlyContain(c => c.ProfileId == profile.Id);
        cats.Should().OnlyContain(c => c.SourceSeedCategoryId.HasValue);

        // exactly the default-enabled ones
        var expectedCount = SeedCategoryCatalog.All.Count(s => s.DefaultEnabled);
        cats.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task Create_with_explicit_seed_ids_copies_only_those()
    {
        using var db = TestDb.CreateAdmin();
        await new SeedCategorySeeder(db.Context).EnsureSeededAsync();
        var seeds = await db.Context.SeedCategories.Take(3).ToListAsync();

        var admin = new ProfileAdminService(db.Context, db.Tenant, TimeProvider.System);
        var profile = await admin.CreateAsync("Alex", "#3B82F6", "person",
            seedCategoryIds: seeds.Select(s => s.Id));

        db.SwitchToProfile(profile.Id);
        var cats = await db.Context.Categories.ToListAsync();

        cats.Should().HaveCount(3);
        cats.Select(c => c.Name).Should().BeEquivalentTo(seeds.Select(s => s.Name));
    }

    [Fact]
    public async Task List_excludes_archived()
    {
        using var db = TestDb.CreateAdmin();
        await new SeedCategorySeeder(db.Context).EnsureSeededAsync();
        var admin = new ProfileAdminService(db.Context, db.Tenant, TimeProvider.System);

        var alive = await admin.CreateAsync("Alive", "#3B82F6", "person");
        var gone = await admin.CreateAsync("Archived", "#EF4444", "person");
        await admin.ArchiveAsync(gone.Id);

        var list = await admin.ListAsync();
        list.Select(p => p.DisplayName).Should().BeEquivalentTo(new[] { "Alive" });
    }

    [Fact]
    public async Task Admin_service_refuses_to_run_from_profile_scope()
    {
        using var db = TestDb.CreateAdmin();
        await new SeedCategorySeeder(db.Context).EnsureSeededAsync();
        var admin = new ProfileAdminService(db.Context, db.Tenant, TimeProvider.System);
        var alex = await admin.CreateAsync("Alex", "#3B82F6", "person");

        db.SwitchToProfile(alex.Id);
        // db.Tenant is the original admin context, but after SwitchToProfile the Context
        // is rebuilt with a different tenant scope. Verify by manually constructing
        // a service bound to a profile-scoped tenant.
        var profileTenant = new MutableTenantContext();
        profileTenant.Set(alex.Id);
        var adminFromProfileScope = new ProfileAdminService(db.Context, profileTenant, TimeProvider.System);

        var act = async () => await adminFromProfileScope.ListAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*admin DI scope*");
    }
}
