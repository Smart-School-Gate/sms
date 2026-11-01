using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-32chars";

    private readonly string _databaseUrl;

    public ApiFactory(string databaseUrl)
    {
        _databaseUrl = databaseUrl;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            DatabaseSecurityCheck.ExpectedDatabaseNameVariable,
            new NpgsqlConnectionStringBuilder(databaseUrl).Database);
        Environment.SetEnvironmentVariable("AUTH_SIGNING_KEY", SigningKey);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", _databaseUrl);
        builder.UseSetting("AUTH_SIGNING_KEY", SigningKey);
        builder.UseSetting("Auth:LoginPermitLimit", "1000");
        builder.UseSetting("Auth:LoginWindowSeconds", "60");
    }
}
