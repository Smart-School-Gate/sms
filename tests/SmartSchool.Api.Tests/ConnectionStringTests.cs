using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

public sealed class ConnectionStringTests
{
    [Fact]
    public void Rejects_marketing_database_without_echoing_the_url()
    {
        const string password = "should-not-appear";
        var uri = $"postgresql://app:{password}@db.example.com/neondb?sslmode=require";
        var error = Assert.Throws<InvalidOperationException>(() => PostgresConnectionStrings.ToNpgsql(uri));
        Assert.Contains("neondb", error.Message);
        Assert.DoesNotContain(password, error.Message);
        Assert.DoesNotContain("db.example.com", error.Message);
    }

    [Fact]
    public void Rejects_keyword_marketing_database()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql("Host=db.example.com;Database=neondb;Username=app;Password=secret"));
        Assert.Contains("neondb", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task Migration_command_refuses_missing_url_and_marketing_database()
    {
        var previous = Environment.GetEnvironmentVariable("MIGRATION_DATABASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("MIGRATION_DATABASE_URL", null);
            Assert.Equal(1, await MigrationRunner.RunAsync());

            Environment.SetEnvironmentVariable(
                "MIGRATION_DATABASE_URL",
                "postgresql://app:should-not-appear@127.0.0.1:1/neondb");
            Assert.Equal(1, await MigrationRunner.RunAsync());

            Environment.SetEnvironmentVariable(
                "MIGRATION_DATABASE_URL",
                "postgresql://owner:should-not-appear@127.0.0.1:1/postgres");
            Assert.Equal(1, await MigrationRunner.RunAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MIGRATION_DATABASE_URL", previous);
        }
    }

    [Fact]
    public void Rejects_maintenance_database_empty_name_and_unknown_ssl_mode()
    {
        const string password = "should-not-appear";
        var maintenance = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"postgresql://app:{password}@db.example.com/postgres"));
        Assert.Contains("postgres", maintenance.Message);
        Assert.DoesNotContain(password, maintenance.Message);

        var missing = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"postgresql://app:{password}@db.example.com/"));
        Assert.DoesNotContain(password, missing.Message);

        var sslMode = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"postgresql://app:{password}@db.example.com/school_override?sslmode=allow-all"));
        Assert.Contains("sslmode", sslMode.Message);
        Assert.DoesNotContain(password, sslMode.Message);
    }

    [Fact]
    public void Applies_verify_full_and_required_channel_binding_without_extra_query_options()
    {
        var verifyFull = PostgresConnectionStrings.ToNpgsql(
            "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=verify-full");
        var verifyBuilder = new NpgsqlConnectionStringBuilder(verifyFull);
        Assert.Equal(SslMode.VerifyFull, verifyBuilder.SslMode);
        Assert.Equal("school_override", verifyBuilder.Database);

        var neon = PostgresConnectionStrings.ToNpgsql(
            "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=require&channel_binding=require");
        var neonBuilder = new NpgsqlConnectionStringBuilder(neon);
        Assert.Equal(SslMode.Require, neonBuilder.SslMode);
        Assert.Equal(ChannelBinding.Require, neonBuilder.ChannelBinding);
        Assert.Equal("school_override", neonBuilder.Database);

        var disabledBinding = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql(
                "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=require&channel_binding=disable"));
        Assert.Contains("channel_binding", disabledBinding.Message);
        Assert.DoesNotContain("should-not-appear", disabledBinding.Message);

        var extra = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql(
                "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=require&channel_binding=require&options=-csearch_path%3Dpublic"));
        Assert.DoesNotContain("should-not-appear", extra.Message);
        Assert.DoesNotContain("options=", extra.Message);

        var marketing = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql(
                "postgresql://app:should-not-appear@db.example.com/neondb?sslmode=require&channel_binding=require"));
        Assert.Contains("neondb", marketing.Message);
        Assert.DoesNotContain("should-not-appear", marketing.Message);
    }

    [Fact]
    public void Runtime_url_comes_from_the_process_environment()
    {
        var previous = Environment.GetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable, null);
            var missing = Assert.Throws<InvalidOperationException>(PostgresConnectionStrings.RequireFromEnvironment);
            Assert.Contains("process environment", missing.Message);

            Environment.SetEnvironmentVariable(
                PostgresConnectionStrings.UrlEnvironmentVariable,
                "postgresql://app:should-not-appear@127.0.0.1/school_override");
            Assert.Equal(
                "school_override",
                PostgresConnectionStrings.DatabaseName(PostgresConnectionStrings.RequireFromEnvironment()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Design_time_factory_ignores_environment_and_remote_targets()
    {
        var previous = Environment.GetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable, null);
            using (var fallback = new AppDbContextFactory().CreateDbContext([]))
                AssertLocalPlaceholder(fallback);

            Environment.SetEnvironmentVariable(
                PostgresConnectionStrings.UrlEnvironmentVariable,
                "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=require");
            using (var remote = new AppDbContextFactory().CreateDbContext([]))
                AssertLocalPlaceholder(remote);

            Environment.SetEnvironmentVariable(
                PostgresConnectionStrings.UrlEnvironmentVariable,
                "postgresql://app:should-not-appear@127.0.0.1/neondb");
            using var blocked = new AppDbContextFactory().CreateDbContext([]);
            AssertLocalPlaceholder(blocked);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PostgresConnectionStrings.UrlEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Rejects_missing_or_unsafe_tls_for_a_non_local_host()
    {
        const string password = "should-not-appear";
        const string host = "db.example.com";

        var missing = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"postgresql://app:{password}@{host}/school_override"));
        Assert.Contains("TLS", missing.Message);
        Assert.DoesNotContain(password, missing.Message);
        Assert.DoesNotContain(host, missing.Message);

        foreach (var mode in new[] { "disable", "allow", "prefer" })
        {
            var unsafeMode = Assert.Throws<InvalidOperationException>(() =>
                PostgresConnectionStrings.ToNpgsql($"postgresql://app:{password}@{host}/school_override?sslmode={mode}"));
            Assert.Contains("TLS", unsafeMode.Message);
            Assert.DoesNotContain(password, unsafeMode.Message);
            Assert.DoesNotContain(host, unsafeMode.Message);
        }

        var keyword = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"Host={host};Database=school_override;Username=app;Password={password};Ssl Mode=Disable"));
        Assert.Contains("TLS", keyword.Message);
        Assert.DoesNotContain(password, keyword.Message);
        Assert.DoesNotContain(host, keyword.Message);

        var omitted = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStrings.ToNpgsql($"Host={host};Database=school_override;Username=app;Password={password}"));
        Assert.Contains("TLS", omitted.Message);
        Assert.DoesNotContain(password, omitted.Message);

        var local = PostgresConnectionStrings.ToNpgsql(
            "Host=127.0.0.1;Database=smart_school;Username=smart_school_app;Password=unused;Ssl Mode=Disable");
        Assert.Equal(SslMode.Disable, new NpgsqlConnectionStringBuilder(local).SslMode);

        var required = PostgresConnectionStrings.ToNpgsql(
            $"postgresql://app:{password}@{host}/school_override?sslmode=require");
        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(required).SslMode);
    }

    [Fact]
    public async Task Migration_command_refuses_a_database_name_that_does_not_match_the_expected_name()
    {
        var previousMigration = Environment.GetEnvironmentVariable("MIGRATION_DATABASE_URL");
        var previousExpected = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "other_database");
            Environment.SetEnvironmentVariable(
                "MIGRATION_DATABASE_URL",
                "postgresql://app:should-not-appear@db.example.com/school_override?sslmode=require");
            Assert.Equal(1, await MigrationRunner.RunAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MIGRATION_DATABASE_URL", previousMigration);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, previousExpected);
        }
    }

    private static void AssertLocalPlaceholder(AppDbContext context)
    {
        var connectionString = context.Database.GetConnectionString();
        Assert.NotNull(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", builder.Host);
        Assert.Equal("smart_school", builder.Database);
        Assert.Equal("smart_school_app", builder.Username);
    }
}
