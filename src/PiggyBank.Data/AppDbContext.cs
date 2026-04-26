using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;
using PiggyBank.Core.Tenancy;

namespace PiggyBank.Data;

public sealed class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>Exposed as an instance member so EF Core global query filters
    /// can reference <c>this.CurrentProfileId</c> — the compiled-model cache
    /// then correctly re-reads the value per query at runtime. If the filter
    /// captured <c>_tenant</c> in an <c>Expression.Constant</c> directly,
    /// EF would bake the first context's tenant into the shared model and
    /// every subsequent context would see the wrong profile.</summary>
    public Guid CurrentProfileId => _tenant.CurrentProfileId ?? Guid.Empty;

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<SeedCategory> SeedCategories => Set<SeedCategory>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<ProfileSettings> ProfileSettings => Set<ProfileSettings>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<RecurringOutgoing> RecurringOutgoings => Set<RecurringOutgoing>();
    public DbSet<Month> Months => Set<Month>();
    public DbSet<MonthlyOutgoing> MonthlyOutgoings => Set<MonthlyOutgoing>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtSnapshot> DebtSnapshots => Set<DebtSnapshot>();
    public DbSet<Pocket> Pockets => Set<Pocket>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<DepositAllocation> DepositAllocations => Set<DepositAllocation>();
    public DbSet<SideIncomeEntry> SideIncomeEntries => Set<SideIncomeEntry>();
    public DbSet<SideIncomeAllocation> SideIncomeAllocations => Set<SideIncomeAllocation>();
    public DbSet<SideIncomeTemplate> SideIncomeTemplates => Set<SideIncomeTemplate>();

    // === Joint (cross-profile, deliberately untenanted) ===
    public DbSet<JointAccount> JointAccounts => Set<JointAccount>();
    public DbSet<JointContribution> JointContributions => Set<JointContribution>();
    public DbSet<JointOutgoing> JointOutgoings => Set<JointOutgoing>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // === System-wide ===
        b.Entity<Profile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
            e.Property(x => x.ColourHex).HasMaxLength(9);
            e.Property(x => x.IconKey).HasMaxLength(40);
            e.HasIndex(x => x.ArchivedAtUtc);
        });

        b.Entity<SeedCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Kind).HasConversion<int>();
            e.HasIndex(x => x.SortOrder);
        });

        b.Entity<AppSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Theme).HasMaxLength(16);
            e.Property(x => x.InstallVersion).HasMaxLength(40);
        });

        // === Profile-owned ===
        b.Entity<ProfileSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DailyFoodBudget).HasColumnType("TEXT");
            e.Property(x => x.BufferPerDay).HasColumnType("TEXT");
            e.Property(x => x.PayCycleDefault).HasConversion<int>();
            e.Property(x => x.SideIncomeTaxBand).HasConversion<int?>();
            e.Property(x => x.SideIncomeTaxCustomRate).HasColumnType("TEXT");
            e.Property(x => x.InvoiceRecipientName).HasMaxLength(120);
            e.Property(x => x.InvoiceSubjectPrefix).HasMaxLength(200);
            e.Property(x => x.InvoiceToEmails).HasMaxLength(500);
            e.Property(x => x.InvoiceCcEmails).HasMaxLength(500);
            e.HasIndex(x => x.ProfileId).IsUnique();
            e.HasOne<Profile>()
                .WithOne()
                .HasForeignKey<ProfileSettings>(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.ColourHex).HasMaxLength(9);
            e.HasIndex(x => new { x.ProfileId, x.IsArchived });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecurringOutgoing>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.DefaultAmount).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.IsArchived, x.SortOrder });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Month>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProfileId, x.PeriodStart }).IsUnique();
            e.Property(x => x.CarriedOverBalance).HasColumnType("TEXT");
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MonthlyOutgoing>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.MonthId });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Month>()
                .WithMany()
                .HasForeignKey(x => x.MonthId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Transaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Payee).IsRequired().HasMaxLength(200);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.Property(x => x.ImportSource).HasMaxLength(60);
            e.HasIndex(x => new { x.ProfileId, x.MonthId, x.Date });
            e.HasIndex(x => new { x.ProfileId, x.CategoryId, x.Date });
            e.HasIndex(x => new { x.ProfileId, x.ImportRunId });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Month>()
                .WithMany()
                .HasForeignKey(x => x.MonthId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Debt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.Limit).HasColumnType("TEXT");
            e.Property(x => x.OpeningBalance).HasColumnType("TEXT");
            e.Property(x => x.AnnualPercentageRate).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.ArchivedAtUtc, x.SortOrder });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DebtSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Balance).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.DebtId, x.SnapshotDate });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Debt>()
                .WithMany()
                .HasForeignKey(x => x.DebtId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Pocket>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.CurrentBalance).HasColumnType("TEXT");
            e.Property(x => x.AutoSavePercent).HasColumnType("TEXT");
            e.Property(x => x.Goal).HasColumnType("TEXT");
            e.Property(x => x.AnnualInterestRate).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.ArchivedAtUtc, x.SortOrder });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Deposit>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.DepositedOn });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DepositAllocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.Property(x => x.AutoSavePercentAtDeposit).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.DepositId });
            e.HasIndex(x => new { x.ProfileId, x.PocketId });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Deposit>()
                .WithMany()
                .HasForeignKey(x => x.DepositId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Pocket>()
                .WithMany()
                .HasForeignKey(x => x.PocketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SideIncomeEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.DurationHours).HasColumnType("TEXT");
            e.Property(x => x.HourlyRate).HasColumnType("TEXT");
            e.Property(x => x.Total).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.PaidOn });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SideIncomeAllocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.Property(x => x.Target).HasConversion<int>();
            e.HasIndex(x => new { x.ProfileId, x.SideIncomeEntryId });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SideIncomeEntry>()
                .WithMany()
                .HasForeignKey(x => x.SideIncomeEntryId)
                .OnDelete(DeleteBehavior.Cascade);
            // Pocket/Month/Transaction FKs are NOT cascading — an allocation
            // should outlive its target record for audit (deleting a pocket
            // shouldn't silently discard the side-income allocation history).
        });

        b.Entity<SideIncomeTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.DurationHours).HasColumnType("TEXT");
            e.Property(x => x.HourlyRate).HasColumnType("TEXT");
            e.Property(x => x.FixedTotal).HasColumnType("TEXT");
            e.HasIndex(x => new { x.ProfileId, x.SortOrder });
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // === Joint (cross-profile, deliberately NOT tenant-filtered) ===
        // These entities are shared between profiles. The household-level
        // view of contributions vs outgoings is the whole point, so a
        // global query filter on ProfileId would defeat the feature.
        b.Entity<JointAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.BankName).HasMaxLength(80);
            e.HasIndex(x => new { x.ArchivedAtUtc, x.SortOrder });
        });

        b.Entity<JointContribution>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.MonthlyAmount).HasColumnType("TEXT");
            e.HasIndex(x => x.JointAccountId);
            e.HasIndex(x => x.ProfileId);
            // Cascade from the joint account: deleting an account naturally
            // discards its contributions.
            e.HasOne<JointAccount>()
                .WithMany()
                .HasForeignKey(x => x.JointAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict on Profile: archiving a profile must not silently
            // drop its joint contribution rows (would silently change the
            // household total). The user has to clear or reassign the row
            // before the profile can be deleted.
            e.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<JointOutgoing>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Amount).HasColumnType("TEXT");
            e.HasIndex(x => new { x.JointAccountId, x.SortOrder });
            e.HasOne<JointAccount>()
                .WithMany()
                .HasForeignKey(x => x.JointAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // === Global query filter: every ProfileOwnedEntity gets one. ===
        // The filter references this.CurrentProfileId (an instance property)
        // so EF Core re-evaluates it per query and the compiled-model cache
        // doesn't bake the wrong profile into the shared model.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (typeof(ProfileOwnedEntity).IsAssignableFrom(entityType.ClrType))
            {
                ApplyProfileFilter(b, entityType.ClrType);
            }
        }
    }

    private void ApplyProfileFilter(ModelBuilder b, Type clrType)
    {
        // e => e.ProfileId == this.CurrentProfileId
        var param = Expression.Parameter(clrType, "e");

        var contextRef = Expression.Constant(this);
        var currentProp = Expression.Property(contextRef, nameof(CurrentProfileId));
        var entityProfileId = Expression.Property(param, nameof(ProfileOwnedEntity.ProfileId));

        var eq = Expression.Equal(entityProfileId, currentProp);
        var lambda = Expression.Lambda(eq, param);

        b.Entity(clrType).HasQueryFilter(lambda);
    }
}
