using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Entities;
using PiggyBank.Core.Tenancy;
using PiggyBank.Data;
using PiggyBank.Data.Profiles;
using PiggyBank.Data.Seeding;
using PiggyBank.Data.Tenancy;

namespace PiggyBank.App.Tests.Infrastructure;

/// <summary>
/// Test double that wraps a real <see cref="IProfileSession"/>-producing
/// DI container backed by a <c>:memory:</c> SQLite database. Tests get the
/// real EF Core pipeline (query filters, tenant-stamp interceptor, real
/// migrations) without the weight of the full WPF host.
///
/// Usage:
///   await using var fake = await FakeProfileSessionManager.CreateAsync();
///   var profileId = await fake.SeedProfileAsync("Alex");
///   fake.OpenProfile(profileId);
///   var vm = new CurrentMonthViewModel(fake, TimeProvider.System, new BudgetCalculator(...));
///   await vm.LoadCommand.ExecuteAsync(null);
/// </summary>
public sealed class FakeProfileSessionManager : IProfileSessionManager, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;
    private ProfileSession? _current;
    private bool _disposed;

    public ProfileSession? Current => _current;

    private FakeProfileSessionManager(SqliteConnection conn, ServiceProvider root)
    {
        _conn = conn;
        _root = root;
    }

    public static async Task<FakeProfileSessionManager> CreateAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        // The DI container's AppDbContext factory honours whatever connection
        // we pass. We route every scope's ctx through THIS connection so the
        // in-memory DB stays alive.
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());
        services.AddScoped<TenantStampInterceptor>();
        services.AddDbContext<AppDbContext>((sp, opts) =>
        {
            opts.UseSqlite(conn);
            opts.AddInterceptors(sp.GetRequiredService<TenantStampInterceptor>());
        });
        services.AddScoped<SeedCategorySeeder>();
        services.AddScoped<ProfileAdminService>();
        services.AddScoped<PiggyBank.Data.Repositories.ICategoryRepository,
            PiggyBank.Data.Repositories.CategoryRepository>();
        services.AddScoped<PiggyBank.Data.Repositories.IRecurringOutgoingRepository,
            PiggyBank.Data.Repositories.RecurringOutgoingRepository>();
        services.AddScoped<PiggyBank.Data.Repositories.IMonthRepository,
            PiggyBank.Data.Repositories.MonthRepository>();
        services.AddScoped<PiggyBank.Data.Repositories.IMonthlyOutgoingRepository,
            PiggyBank.Data.Repositories.MonthlyOutgoingRepository>();
        services.AddScoped<PiggyBank.Data.Repositories.ITransactionRepository,
            PiggyBank.Data.Repositories.TransactionRepository>();
        services.AddScoped<PiggyBank.Data.Services.MonthService>();

        var root = services.BuildServiceProvider();

        // Apply migrations + seed the catalog once, in an admin scope.
        using (var admin = ProfileSession.AdminScope(root))
        {
            var db = admin.Services.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await admin.Services.GetRequiredService<SeedCategorySeeder>()
                .EnsureSeededAsync();
        }

        return new FakeProfileSessionManager(conn, root);
    }

    /// <summary>Creates a profile, returns its id. Defaults enabled seed categories.</summary>
    public async Task<Guid> SeedProfileAsync(string displayName = "Alex")
    {
        using var admin = ProfileSession.AdminScope(_root);
        var service = admin.Services.GetRequiredService<ProfileAdminService>();
        var profile = await service.CreateAsync(displayName, "#3B82F6", "person");
        return profile.Id;
    }

    public Task EnsureInitialisedAsync(CancellationToken ct = default)
    {
        // Already initialised in CreateAsync.
        return Task.CompletedTask;
    }

    public void OpenProfile(Guid profileId)
    {
        _current?.Dispose();
        _current = new ProfileSession(_root, profileId);
    }

    public ProfileSession OpenAdminScope() => ProfileSession.AdminScope(_root);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _current?.Dispose();
        await _root.DisposeAsync();
        _conn.Close();
        _conn.Dispose();
    }
}
