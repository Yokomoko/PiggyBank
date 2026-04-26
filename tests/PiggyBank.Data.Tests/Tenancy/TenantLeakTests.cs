using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Tests.Tenancy;

/// <summary>
/// The primary multi-tenancy safety gate. Reflects over every public
/// <c>DbSet&lt;T&gt;</c> on <see cref="AppDbContext"/> where
/// <c>T : ProfileOwnedEntity</c>, SEEDS AT LEAST ONE ROW PER DISCOVERED
/// TYPE IN BOTH PROFILES (no vacuous passes), then asserts that a query
/// from profile B never returns rows owned by profile A.
///
/// Runs before the <c>data-tests</c> job in CI; blocks merge on failure.
/// When a new <see cref="ProfileOwnedEntity"/> is added to the DbContext,
/// this test automatically covers it — no maintenance needed.
/// </summary>
public sealed class TenantLeakTests
{
    /// <summary>The suite currently asserts on at least this many tenant
    /// DbSets. If the production DbContext drops below this, something's
    /// been accidentally untyped or removed. If it grows, bump this
    /// number after verifying the new entity is genuinely tenant-owned.</summary>
    private const int MinimumExpectedTenantDbSets = 6;

    [Fact]
    public async Task No_DbSet_returns_rows_from_other_profile()
    {
        using var db = TestDb.CreateAdmin();

        var profileA = new Profile { DisplayName = "Alex" };
        var profileB = new Profile { DisplayName = "Partner" };
        db.Context.Profiles.AddRange(profileA, profileB);
        await db.Context.SaveChangesAsync();

        var ownedSets = DiscoverOwnedDbSets();
        ownedSets.Should().HaveCountGreaterThanOrEqualTo(MinimumExpectedTenantDbSets,
            "if coverage dropped, a ProfileOwnedEntity was accidentally converted or removed");

        // Seed at least one row per discovered DbSet in BOTH profiles,
        // so the final assertions can never pass vacuously.
        db.SwitchToProfile(profileA.Id);
        await SeedEveryOwnedSetAsync(db, ownedSets, tag: "_A");

        db.SwitchToProfile(profileB.Id);
        await SeedEveryOwnedSetAsync(db, ownedSets, tag: "_B");

        // Now, standing in profile B's shoes, enumerate every DbSet<T> and
        // assert zero rows belong to A. The seeding above guarantees each
        // set has at least ONE row visible — so a broken filter (returning
        // nothing for anything) still fails instead of vacuously passing.
        foreach (var setInfo in ownedSets)
        {
            var set = setInfo.Property.GetValue(db.Context)!;
            var toListAsync = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .First(m => m.Name == "ToListAsync" && m.GetParameters().Length == 2)
                .MakeGenericMethod(setInfo.ElementType);

            var task = (Task)toListAsync.Invoke(null, [set, CancellationToken.None])!;
            await task;
            var result = (System.Collections.IEnumerable)task.GetType()
                .GetProperty("Result")!.GetValue(task)!;

            var rows = new List<ProfileOwnedEntity>();
            foreach (var item in result) rows.Add((ProfileOwnedEntity)item);

            rows.Should().NotBeEmpty(
                $"DbSet<{setInfo.ElementType.Name}> must see B's own seeded row; " +
                "if empty the filter is over-aggressive and this test is vacuous");

            rows.Should().OnlyContain(r => r.ProfileId == profileB.Id,
                $"DbSet<{setInfo.ElementType.Name}> leaked A-owned rows into profile B's scope");
        }
    }

    private static IReadOnlyList<OwnedDbSetInfo> DiscoverOwnedDbSets() =>
        typeof(AppDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                        && typeof(ProfileOwnedEntity).IsAssignableFrom(
                               p.PropertyType.GetGenericArguments()[0]))
            .Select(p => new OwnedDbSetInfo(p, p.PropertyType.GetGenericArguments()[0]))
            .ToList();

    /// <summary>
    /// For each discovered tenant-scoped entity, inserts one row using a
    /// default-constructed instance with the minimal required fields set.
    /// Grows with the domain — new entities need a case here, but the test
    /// itself fails loudly if it hits an unrecognised type (no silent skip).
    /// </summary>
    private static async Task SeedEveryOwnedSetAsync(TestDb db, IReadOnlyList<OwnedDbSetInfo> sets, string tag)
    {
        var month = new Month
        {
            PeriodStart = new DateOnly(2026, 3, 25),
            PeriodEnd = new DateOnly(2026, 4, 24),
            LastPayday = new DateOnly(2026, 3, 25),
            NextPayday = new DateOnly(2026, 4, 25),
            Notes = tag,
        };
        db.Context.Months.Add(month);
        await db.Context.SaveChangesAsync();

        // Debt must exist before DebtSnapshot can reference it.
        var debt = new Debt
        {
            Name = "Card" + tag,
            Kind = DebtKind.CreditCard,
            OpeningBalance = -500m,
        };
        db.Context.Debts.Add(debt);
        await db.Context.SaveChangesAsync();

        // Pocket must exist before DepositAllocation can reference it.
        var pocket = new Pocket
        {
            Name = "Pocket" + tag,
            CurrentBalance = 1000m,
            AutoSavePercent = 100m,
            AnnualInterestRate = 0.05m,
        };
        db.Context.Pockets.Add(pocket);
        await db.Context.SaveChangesAsync();

        // Deposit must exist before DepositAllocation.
        var deposit = new Deposit
        {
            DepositedOn = new DateOnly(2026, 4, 1),
            Amount = 100m,
            RecordedAtUtc = DateTime.UtcNow,
        };
        db.Context.Deposits.Add(deposit);
        await db.Context.SaveChangesAsync();

        db.Context.DepositAllocations.Add(new DepositAllocation
        {
            DepositId = deposit.Id,
            PocketId = pocket.Id,
            AutoSavePercentAtDeposit = 100m,
            Amount = 100m,
        });
        await db.Context.SaveChangesAsync();

        // Side-income entry must exist before SideIncomeAllocation can reference it.
        var sideEntry = new SideIncomeEntry
        {
            PaidOn = new DateOnly(2026, 3, 27),
            Total = 50m,
            CreatedAtUtc = DateTime.UtcNow,
            Description = "Cash job" + tag,
        };
        db.Context.SideIncomeEntries.Add(sideEntry);
        await db.Context.SaveChangesAsync();

        db.Context.SideIncomeAllocations.Add(new SideIncomeAllocation
        {
            SideIncomeEntryId = sideEntry.Id,
            Amount = 50m,
            Target = SideIncomeAllocationTarget.Pocket,
            PocketId = pocket.Id,
            AllocatedAtUtc = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        foreach (var (_, elementType) in sets)
        {
            var typeName = elementType.Name;
            ProfileOwnedEntity entity = typeName switch
            {
                nameof(Category) => new Category { Name = "Food" + tag, Kind = CategoryKind.Spend },
                nameof(ProfileSettings) => new ProfileSettings(),
                nameof(RecurringOutgoing) => new RecurringOutgoing { Name = "Rent" + tag, DefaultAmount = -1000m },
                nameof(Month) => null!,  // already seeded above
                nameof(MonthlyOutgoing) => new MonthlyOutgoing { MonthId = month.Id, Name = "Rent" + tag, Amount = -1000m },
                nameof(Transaction) => new Transaction
                {
                    MonthId = month.Id,
                    Date = new DateOnly(2026, 4, 5),
                    Payee = "Tesco" + tag,
                    Amount = -20m,
                },
                nameof(Debt) => null!,  // already seeded above
                nameof(DebtSnapshot) => new DebtSnapshot
                {
                    DebtId = debt.Id,
                    SnapshotDate = new DateOnly(2026, 4, 20),
                    Balance = -450m,
                },
                nameof(Pocket) => null!,   // seeded below
                nameof(Deposit) => null!,  // seeded below
                nameof(DepositAllocation) => null!,  // seeded below
                nameof(SideIncomeEntry) => null!,  // seeded below
                nameof(SideIncomeAllocation) => null!,  // seeded below
                nameof(SideIncomeTemplate) => new SideIncomeTemplate { Name = "Cleaning shift" + tag },
                _ => throw new InvalidOperationException(
                    $"TenantLeakTests.SeedEveryOwnedSetAsync has no case for {typeName}. " +
                    $"Add one when introducing a new ProfileOwnedEntity, otherwise the test will still fail loudly — which is the point."),
            };

            if (entity is not null)
                db.Context.Add(entity);
        }

        await db.Context.SaveChangesAsync();
    }

    private sealed record OwnedDbSetInfo(PropertyInfo Property, Type ElementType);
}
