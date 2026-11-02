using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSchool.Infrastructure.Persistence;

public static class SchoolDatabase
{
    public const string MigrationsAssembly = "SmartSchool.Infrastructure";

    public static DbContextOptionsBuilder<AppDbContext> UseSchoolPostgres(
        this DbContextOptionsBuilder<AppDbContext> options,
        string connectionString)
    {
        Configure(options, connectionString);
        return options;
    }

    public static IServiceCollection AddSchoolPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IDbSession, DbSession>();
        services.AddSingleton<TenantCommandInterceptor>();
        services.AddSingleton<TenantConnectionInterceptor>();
        services.AddSingleton<TenantTransactionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            Configure(options, connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<TenantConnectionInterceptor>(),
                sp.GetRequiredService<TenantTransactionInterceptor>(),
                sp.GetRequiredService<TenantCommandInterceptor>());
        });
        services.AddHostedService<DatabaseSecurityCheck>();
        return services;
    }

    private static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly));
        options.UseSnakeCaseNamingConvention();
    }
}
