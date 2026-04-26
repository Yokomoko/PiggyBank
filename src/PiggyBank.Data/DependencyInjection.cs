using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PiggyBank.Core.Tenancy;
using PiggyBank.Data.Profiles;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Seeding;
using PiggyBank.Data.Services;
using PiggyBank.Data.Tenancy;

namespace PiggyBank.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddPiggyBankData(
        this IServiceCollection services,
        string sqliteConnectionString)
    {
        // Ensure a TimeProvider is registered (the App host also registers one;
        // this guards against callers who bypass AppHost.Build and talk to Data directly).
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());
        services.AddScoped<TenantStampInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opts) =>
        {
            opts.UseSqlite(sqliteConnectionString);
            opts.AddInterceptors(sp.GetRequiredService<TenantStampInterceptor>());
        });

        services.AddScoped<SeedCategorySeeder>();
        services.AddScoped<ProfileAdminService>();

        // Repositories — each a thin per-entity facade over AppDbContext.
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IRecurringOutgoingRepository, RecurringOutgoingRepository>();
        services.AddScoped<IMonthRepository, MonthRepository>();
        services.AddScoped<IMonthlyOutgoingRepository, MonthlyOutgoingRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        services.AddScoped<IDebtRepository, DebtRepository>();
        services.AddScoped<IPocketRepository, PocketRepository>();
        services.AddScoped<IDepositRepository, DepositRepository>();
        services.AddScoped<DepositService>();
        services.AddScoped<ISideIncomeRepository, SideIncomeRepository>();
        services.AddScoped<IJointRepository, JointRepository>();

        // Services that orchestrate multiple repositories.
        services.AddScoped<MonthService>();
        services.AddScoped<SideIncomeService>();

        return services;
    }
}
