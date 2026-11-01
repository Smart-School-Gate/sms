using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SmartSchool.Infrastructure.Persistence;

public static class MigrationRunner
{
    public static async Task<int> RunAsync()
    {
        var migrationUrl = Environment.GetEnvironmentVariable("MIGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(migrationUrl))
        {
            Console.Error.WriteLine("MIGRATION_DATABASE_URL is required. Refusing to fall back to DATABASE_URL.");
            return 1;
        }

        if (PostgresConnectionStrings.IsMarketingDatabase(migrationUrl))
        {
            Console.Error.WriteLine("Refusing to migrate the marketing database neondb.");
            return 1;
        }

        var databaseName = PostgresConnectionStrings.DatabaseName(migrationUrl);
        if (string.IsNullOrWhiteSpace(databaseName)
            || string.Equals(databaseName, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("MIGRATION_DATABASE_URL must name the school's own database. Refusing to create or migrate the postgres maintenance database.");
            return 1;
        }

        var expectedDatabase = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        if (!string.Equals(databaseName, expectedDatabase, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Refusing to migrate because the database name does not match EXPECTED_DATABASE_NAME.");
            return 1;
        }

        try
        {
            var connectionString = PostgresConnectionStrings.ToNpgsql(migrationUrl);
            var builder = new NpgsqlConnectionStringBuilder(connectionString);

            await using (var verify = new NpgsqlConnection(builder.ConnectionString))
            {
                await verify.OpenAsync();
                await using var current = new NpgsqlCommand("SELECT current_database()", verify);
                var actual = (string?)await current.ExecuteScalarAsync();
                if (!string.Equals(actual, expectedDatabase, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Refusing to migrate because the connected database does not match EXPECTED_DATABASE_NAME.");
                    return 1;
                }
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSchoolPostgres(builder.ConnectionString)
                .Options;
            await using var db = new AppDbContext(options, new DbSession());
            await db.Database.MigrateAsync();
            Console.WriteLine($"Migrations applied to database {builder.Database}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed: {ex.GetType().Name}");
            return 1;
        }
    }
}
