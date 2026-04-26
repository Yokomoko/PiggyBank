using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Tests.Tenancy;

public sealed class TenantStampInterceptorTests
{
    [Fact]
    public async Task Insert_stamps_current_profile_id_when_unset()
    {
        using var db = TestDb.CreateAdmin();
        var profile = new Profile { DisplayName = "Alex" };
        db.Context.Profiles.Add(profile);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(profile.Id);

        db.Context.Categories.Add(new Category { Name = "Food", Kind = CategoryKind.Spend });
        await db.Context.SaveChangesAsync();

        var saved = await db.Context.Categories.IgnoreQueryFilters().SingleAsync();
        saved.ProfileId.Should().Be(profile.Id);
    }

    [Fact]
    public async Task Insert_without_tenant_scope_throws()
    {
        using var db = TestDb.CreateAdmin();
        // admin scope has no profile — inserting a ProfileOwnedEntity must fail.
        db.Context.Categories.Add(new Category { Name = "Food", Kind = CategoryKind.Spend });

        var act = async () => await db.Context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No profile in scope*");
    }

    [Fact]
    public async Task Insert_with_mismatching_profile_id_throws()
    {
        using var db = TestDb.CreateAdmin();
        var alex = new Profile { DisplayName = "Alex" };
        var partner = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(alex, partner);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(alex.Id);

        // Hostile-actor scenario: code tries to insert a Category for a
        // DIFFERENT profile than the current scope. The interceptor must
        // catch this — the prior review flagged the branch as untested.
        db.Context.Categories.Add(new Category
        {
            Name = "Food",
            Kind = CategoryKind.Spend,
            ProfileId = partner.Id,  // wrong profile
        });

        var act = async () => await db.Context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*but current scope is*");
    }

    [Fact]
    public async Task Updating_profile_id_throws()
    {
        using var db = TestDb.CreateAdmin();
        var a = new Profile { DisplayName = "Alex" };
        var b = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(a, b);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(a.Id);
        var cat = new Category { Name = "Food", Kind = CategoryKind.Spend };
        db.Context.Categories.Add(cat);
        await db.Context.SaveChangesAsync();

        // Try to move the category to profile B — must throw.
        cat.ProfileId = b.Id;
        var act = async () => await db.Context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProfileId is immutable*");
    }
}
