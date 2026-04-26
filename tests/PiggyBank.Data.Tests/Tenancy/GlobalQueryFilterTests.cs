using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Tests.Tenancy;

public sealed class GlobalQueryFilterTests
{
    [Fact]
    public async Task Query_from_profile_B_never_sees_profile_A_rows()
    {
        using var db = TestDb.CreateAdmin();

        var a = new Profile { DisplayName = "Alex" };
        var b = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(a, b);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(a.Id);
        db.Context.Categories.Add(new Category { Name = "Food_A", Kind = CategoryKind.Spend });
        db.Context.Categories.Add(new Category { Name = "Petrol_A", Kind = CategoryKind.Spend });
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(b.Id);
        db.Context.Categories.Add(new Category { Name = "Food_B", Kind = CategoryKind.Spend });
        await db.Context.SaveChangesAsync();

        // Looking from B's scope, A's categories must be invisible.
        var visible = await db.Context.Categories.ToListAsync();
        visible.Should().OnlyContain(c => c.Name == "Food_B");
    }

    [Fact]
    public async Task IgnoreQueryFilters_can_enumerate_both_profiles_rows()
    {
        using var db = TestDb.CreateAdmin();
        var a = new Profile { DisplayName = "Alex" };
        var b = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(a, b);
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(a.Id);
        db.Context.Categories.Add(new Category { Name = "FromA", Kind = CategoryKind.Spend });
        await db.Context.SaveChangesAsync();

        db.SwitchToProfile(b.Id);
        db.Context.Categories.Add(new Category { Name = "FromB", Kind = CategoryKind.Spend });
        await db.Context.SaveChangesAsync();

        // Admin bypass sees both — proves the opt-out mechanism works.
        var all = await db.Context.Categories.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
        all.Select(c => c.Name).Should().BeEquivalentTo(new[] { "FromA", "FromB" });
    }
}
