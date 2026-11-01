using Npgsql;

namespace SmartSchool.Infrastructure.Persistence;

public static class PostgresConnectionStrings
{
    public const string UrlEnvironmentVariable = "DATABASE_URL";

    public static string RequireFromEnvironment()
    {
        var databaseUrl = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException(
                "DATABASE_URL must be set in the process environment. Configuration files and requests cannot supply the database connection.");
        }

        return databaseUrl;
    }

    public static string ToNpgsql(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is required.");

        if (IsKeywordConnectionString(databaseUrl))
        {
            var keyword = new NpgsqlConnectionStringBuilder(databaseUrl);
            RejectDisallowedDatabase(keyword.Database);
            RequireTransportSecurity(keyword);
            return keyword.ConnectionString;
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || uri.Host.Length == 0
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres"))
        {
            throw new InvalidOperationException("DATABASE_URL is not a PostgreSQL connection string.");
        }

        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        var separator = userInfo.IndexOf(':');
        var username = separator >= 0 ? userInfo[..separator] : userInfo;
        var password = separator >= 0 ? userInfo[(separator + 1)..] : "";
        var database = uri.AbsolutePath.Trim('/');
        RejectDisallowedDatabase(database);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database,
            Username = username,
            Password = password
        };

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("sslmode", out var sslMode))
            builder.SslMode = ParseSslMode(sslMode);
        if (query.TryGetValue("channel_binding", out var channelBinding))
            builder.ChannelBinding = ParseChannelBinding(channelBinding);

        var unsupported = query.Keys
            .Where(key => !key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("channel_binding", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException(
                "DATABASE_URL contains a query option the API does not apply. Use a keyword connection string for additional Npgsql options.");
        }

        RequireTransportSecurity(builder);
        return builder.ConnectionString;
    }

    public static string DatabaseName(string databaseUrl)
    {
        if (IsKeywordConnectionString(databaseUrl))
            return new NpgsqlConnectionStringBuilder(databaseUrl).Database ?? "";

        if (Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
            return uri.AbsolutePath.Trim('/');

        return "";
    }

    public static bool IsMarketingDatabase(string databaseUrl) =>
        string.Equals(DatabaseName(databaseUrl), "neondb", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeywordConnectionString(string value) =>
        value.Contains("Host=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Server=", StringComparison.OrdinalIgnoreCase);

    private static SslMode ParseSslMode(string sslMode) => sslMode.Trim().ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "require" => SslMode.Require,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => throw new InvalidOperationException("DATABASE_URL sslmode is not recognized.")
    };

    private static ChannelBinding ParseChannelBinding(string channelBinding) => channelBinding.Trim().ToLowerInvariant() switch
    {
        "require" => ChannelBinding.Require,
        _ => throw new InvalidOperationException("DATABASE_URL channel_binding must be require.")
    };

    private static void RequireTransportSecurity(NpgsqlConnectionStringBuilder builder)
    {
        if (IsLoopback(builder.Host))
            return;

        if (builder.SslMode is SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull)
            return;

        throw new InvalidOperationException(
            "DATABASE_URL must require TLS when the host is not local. Use sslmode=require, verify-ca, or verify-full.");
    }

    private static bool IsLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var value = host.Trim().Trim('[', ']');
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("127.0.0.1", StringComparison.Ordinal)
            || value.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectDisallowedDatabase(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("DATABASE_URL must name this school's own database.");

        if (string.Equals(database, "neondb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to use the marketing database neondb. Set DATABASE_URL to this school's dedicated database.");
        }

        if (string.Equals(database, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to use the postgres maintenance database. Set DATABASE_URL to this school's dedicated database.");
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
            return values;

        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator < 0)
                continue;
            values[Uri.UnescapeDataString(part[..separator])] = Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        return values;
    }
}
