using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Tenancy;
using PiggyBank.Data;
using PiggyBank.Data.Tenancy;

namespace PiggyBank.Data.Tests.Tenancy;

/// <summary>
/// Builds a real SQLite <c>:memory:</c> database for integration tests.
/// Uses a live <c>SqliteConnection</c> held by the caller so the DB
/// isn't dropped when the first context is disposed.
/// </summary>
/// <remarks>
/// Runs EF Core migrations (NOT <c>EnsureCreated</c>) so migration bugs
/// are caught by tests. Anything that works against <c>EnsureCreated</c>
/// but breaks on <c>Migrate</c> is a silent prod hazard.
/// </remarks>
internal sealed class TestDb : IDisposable
{
    public SqliteConnection Connection { get; }
    public MutableTenantContext Tenant { get; private set; }
    public AppDbContext Context { get; private set; }

    private TestDb(SqliteConnection conn, MutableTenantContext tenant, AppDbContext ctx)
    {
        Connection = conn;
        Tenant = tenant;
        Context = ctx;
    }

    public static TestDb CreateAdmin()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = new MutableTenantContext();
        tenant.SetAdminScope();

        var ctx = BuildContext(conn, tenant);
        ctx.Database.Migrate();  // real migrations, not EnsureCreated — catches migration bugs

        return new TestDb(conn, tenant, ctx);
    }

    /// <summary>
    /// Switch the context to a given profile. Equivalent to starting a
    /// fresh <c>ProfileSession</c> — the old context is disposed and a
    /// new one is built against the same in-memory DB. The public
    /// <see cref="Tenant"/> property is updated to match the new scope.
    /// </summary>
    public void SwitchToProfile(Guid profileId)
    {
        Context.Dispose();
        Tenant = new MutableTenantContext();
        Tenant.Set(profileId);
        Context = BuildContext(Connection, Tenant);
    }

    public void SwitchToAdminScope()
    {
        Context.Dispose();
        Tenant = new MutableTenantContext();
        Tenant.SetAdminScope();
        Context = BuildContext(Connection, Tenant);
    }

    private static AppDbContext BuildContext(SqliteConnection conn, MutableTenantContext tenant)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(new TenantStampInterceptor(tenant))
            .Options;
        return new AppDbContext(opts, tenant);
    }

    public void Dispose()
    {
        Context.Dispose();
        Connection.Close();
        Connection.Dispose();
    }
}
