using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Tests.Tenancy;

namespace PiggyBank.Data.Tests.Joint;

public sealed class JointRepositoryTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Add_and_list_round_trips_the_account()
    {
        using var db = TestDb.CreateAdmin();
        var repo = new JointRepository(db.Context, Clock);

        await repo.AddAccountAsync(new JointAccount
        {
            Name = "Joint bills",
            BankName = "Nationwide",
            Notes = "Main household account",
        });

        var list = await repo.ListAccountsAsync();
        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Joint bills");
        list[0].BankName.Should().Be("Nationwide");
    }

    [Fact]
    public async Task Archive_hides_account_from_default_list()
    {
        using var db = TestDb.CreateAdmin();
        var repo = new JointRepository(db.Context, Clock);

        var alive = await repo.AddAccountAsync(new JointAccount { Name = "Alive" });
        var gone = await repo.AddAccountAsync(new JointAccount { Name = "Gone" });
        await repo.ArchiveAccountAsync(gone.Id);

        var defaultList = await repo.ListAccountsAsync();
        defaultList.Select(a => a.Name).Should().BeEquivalentTo(new[] { "Alive" });

        var withArchived = await repo.ListAccountsAsync(includeArchived: true);
        withArchived.Should().HaveCount(2);
        withArchived.Single(a => a.Name == "Gone").ArchivedAtUtc
            .Should().Be(new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Contributions_are_filtered_by_account()
    {
        using var db = TestDb.CreateAdmin();
        var (alice, bob) = await SeedTwoProfilesAsync(db);
        var repo = new JointRepository(db.Context, Clock);

        var account1 = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var account2 = await repo.AddAccountAsync(new JointAccount { Name = "Holiday" });

        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account1.Id, ProfileId = alice.Id, MonthlyAmount = 500m,
        });
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account1.Id, ProfileId = bob.Id, MonthlyAmount = 400m,
        });
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account2.Id, ProfileId = alice.Id, MonthlyAmount = 50m,
        });

        var bills = await repo.ListContributionsAsync(account1.Id);
        bills.Should().HaveCount(2);
        bills.Sum(c => c.MonthlyAmount).Should().Be(900m);

        var holiday = await repo.ListContributionsAsync(account2.Id);
        holiday.Should().HaveCount(1);
        holiday[0].MonthlyAmount.Should().Be(50m);
    }

    [Fact]
    public async Task Outgoings_are_filtered_by_account_and_ordered()
    {
        using var db = TestDb.CreateAdmin();
        var repo = new JointRepository(db.Context, Clock);

        var account1 = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var account2 = await repo.AddAccountAsync(new JointAccount { Name = "Holiday" });

        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account1.Id, Name = "Rent", Amount = -1200m, SortOrder = 0,
        });
        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account1.Id, Name = "Council tax", Amount = -180m, SortOrder = 1,
        });
        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account2.Id, Name = "Flights", Amount = -350m,
        });

        var bills = await repo.ListOutgoingsAsync(account1.Id);
        bills.Should().HaveCount(2);
        bills.Select(o => o.Name).Should().ContainInOrder("Rent", "Council tax");
        bills.Sum(o => o.Amount).Should().Be(-1380m);

        var holiday = await repo.ListOutgoingsAsync(account2.Id);
        holiday.Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_contribution_persists_new_amount()
    {
        using var db = TestDb.CreateAdmin();
        var (alice, _) = await SeedTwoProfilesAsync(db);
        var repo = new JointRepository(db.Context, Clock);

        var account = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var contribution = await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = alice.Id, MonthlyAmount = 500m,
        });

        contribution.MonthlyAmount = 650m;
        await repo.UpdateContributionAsync(contribution);

        var roundtrip = await repo.ListContributionsAsync(account.Id);
        roundtrip.Single().MonthlyAmount.Should().Be(650m);
    }

    [Fact]
    public async Task Delete_contribution_removes_only_that_row()
    {
        using var db = TestDb.CreateAdmin();
        var (alice, bob) = await SeedTwoProfilesAsync(db);
        var repo = new JointRepository(db.Context, Clock);

        var account = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var aliceRow = await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = alice.Id, MonthlyAmount = 500m,
        });
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = bob.Id, MonthlyAmount = 400m,
        });

        await repo.DeleteContributionAsync(aliceRow.Id);

        var remaining = await repo.ListContributionsAsync(account.Id);
        remaining.Should().HaveCount(1);
        remaining[0].ProfileId.Should().Be(bob.Id);
    }

    [Fact]
    public async Task Update_outgoing_persists_changes()
    {
        using var db = TestDb.CreateAdmin();
        var repo = new JointRepository(db.Context, Clock);

        var account = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var rent = await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account.Id, Name = "Rent", Amount = -1200m,
        });

        rent.Amount = -1300m;
        rent.Name = "Rent (increased)";
        await repo.UpdateOutgoingAsync(rent);

        var roundtrip = (await repo.ListOutgoingsAsync(account.Id)).Single();
        roundtrip.Name.Should().Be("Rent (increased)");
        roundtrip.Amount.Should().Be(-1300m);
    }

    [Fact]
    public async Task Delete_outgoing_removes_only_that_row()
    {
        using var db = TestDb.CreateAdmin();
        var repo = new JointRepository(db.Context, Clock);

        var account = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        var rent = await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account.Id, Name = "Rent", Amount = -1200m,
        });
        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account.Id, Name = "Council tax", Amount = -180m,
        });

        await repo.DeleteOutgoingAsync(rent.Id);

        var remaining = await repo.ListOutgoingsAsync(account.Id);
        remaining.Should().HaveCount(1);
        remaining[0].Name.Should().Be("Council tax");
    }

    [Fact]
    public async Task Cascade_delete_clears_contributions_and_outgoings_when_account_removed()
    {
        using var db = TestDb.CreateAdmin();
        var (alice, bob) = await SeedTwoProfilesAsync(db);
        var repo = new JointRepository(db.Context, Clock);

        var account = await repo.AddAccountAsync(new JointAccount { Name = "Bills" });
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = alice.Id, MonthlyAmount = 500m,
        });
        await repo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = bob.Id, MonthlyAmount = 400m,
        });
        await repo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account.Id, Name = "Rent", Amount = -1200m,
        });

        // Hard-delete the account directly to verify ON DELETE CASCADE.
        var entity = await db.Context.JointAccounts.FirstAsync(a => a.Id == account.Id);
        db.Context.JointAccounts.Remove(entity);
        await db.Context.SaveChangesAsync();

        (await db.Context.JointContributions.ToListAsync()).Should().BeEmpty(
            "deleting a joint account must cascade to its contributions");
        (await db.Context.JointOutgoings.ToListAsync()).Should().BeEmpty(
            "deleting a joint account must cascade to its outgoings");
    }

    [Fact]
    public async Task Joint_data_is_visible_from_either_profile_scope()
    {
        // The whole point of the feature: Alice writes a joint outgoing,
        // Bob's session sees it, no scope-switching dance required.
        using var db = TestDb.CreateAdmin();
        var (alice, bob) = await SeedTwoProfilesAsync(db);

        // Stand in Alice's shoes and write an account + a row.
        db.SwitchToProfile(alice.Id);
        var aliceRepo = new JointRepository(db.Context, Clock);
        var account = await aliceRepo.AddAccountAsync(new JointAccount { Name = "Joint bills" });
        await aliceRepo.AddContributionAsync(new JointContribution
        {
            JointAccountId = account.Id, ProfileId = alice.Id, MonthlyAmount = 500m,
        });
        await aliceRepo.AddOutgoingAsync(new JointOutgoing
        {
            JointAccountId = account.Id, Name = "Rent", Amount = -1200m,
        });

        // Switch to Bob and verify everything is still visible.
        db.SwitchToProfile(bob.Id);
        var bobRepo = new JointRepository(db.Context, Clock);
        var accounts = await bobRepo.ListAccountsAsync();
        accounts.Should().HaveCount(1, "joint accounts are not tenant-filtered");
        accounts[0].Name.Should().Be("Joint bills");

        var contributions = await bobRepo.ListContributionsAsync(account.Id);
        contributions.Should().HaveCount(1);
        contributions[0].ProfileId.Should().Be(alice.Id);

        var outgoings = await bobRepo.ListOutgoingsAsync(account.Id);
        outgoings.Should().HaveCount(1);
        outgoings[0].Name.Should().Be("Rent");
    }

    private static async Task<(Profile alice, Profile bob)> SeedTwoProfilesAsync(TestDb db)
    {
        var alice = new Profile { DisplayName = "Alice" };
        var bob = new Profile { DisplayName = "Bob" };
        db.Context.Profiles.AddRange(alice, bob);
        await db.Context.SaveChangesAsync();
        return (alice, bob);
    }
}
