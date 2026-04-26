using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PiggyBank.Core.Tenancy;

namespace PiggyBank.Data;

/// <summary>
/// Supplies the EF tooling (<c>dotnet ef migrations add</c>) with an
/// <see cref="AppDbContext"/> instance. The tenant context is a stub —
/// migrations don't run queries, just inspect the model.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new AppDbContext(opts, new StubTenantContext());
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? CurrentProfileId => null;
        public bool IsAdminScope => true;
    }
}
