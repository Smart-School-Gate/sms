using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StartupAvailabilityTests
{
    private readonly PostgresFixture _postgres;

    public StartupAvailabilityTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Startup_continues_when_postgresql_is_unreachable()
    {
        var databaseUrl = UnreachableDatabaseUrl();
        await using var factory = new LoggingApiFactory(databaseUrl);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        using var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var liveBody = await live.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", liveBody, StringComparison.Ordinal);
        Assert.Contains("live", liveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("database", liveBody, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Contains("Unhealthy", readyBody, StringComparison.Ordinal);
        Assert.Contains("ready", readyBody, StringComparison.Ordinal);
        Assert.Contains("database", readyBody, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Contains("database", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var deferred = factory.Logs.Messages
            .Where(message => message.Contains("deferred", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(deferred);
        var logged = string.Join('\n', deferred);
        Assert.Contains("unreachable", logged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(databaseUrl, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", logged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unused", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_ready_is_healthy_when_postgresql_is_available()
    {
        await using var factory = new ApiFactory(_postgres.AppConnectionString);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Contains("live", await live.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Contains("ready", readyBody, StringComparison.Ordinal);
        Assert.Contains("database", readyBody, StringComparison.Ordinal);
        Assert.Contains("Healthy", readyBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_rejects_an_authentication_failure()
    {
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        var hbaFile = (string)(await ScalarAsync(owner, "SHOW hba_file") ?? "");
        var original = await File.ReadAllTextAsync(hbaFile);
        try
        {
            await File.WriteAllTextAsync(hbaFile, RequireScram(original));
            await ScalarAsync(owner, "SELECT pg_reload_conf()");
            NpgsqlConnection.ClearAllPools();

            var rejected = new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString)
            {
                Password = "wrong-password"
            };
            using var factory = new ApiFactory(rejected.ConnectionString);
            var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            var postgres = Flatten(error).OfType<PostgresException>().FirstOrDefault();
            var chain = string.Join(" | ", Flatten(error).Select(item => item.GetType().Name));
            Assert.True(postgres is not null, "Expected an authentication error. Exception chain: " + chain);
            Assert.StartsWith("28", postgres.SqlState, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(hbaFile, original);
            await ScalarAsync(owner, "SELECT pg_reload_conf()");
            NpgsqlConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public async Task Startup_rejects_a_row_level_security_failure()
    {
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var drop = new NpgsqlCommand("DROP POLICY branches_tenant ON branches", owner))
            await drop.ExecuteNonQueryAsync();
        await using (var tamper = new NpgsqlCommand(
            "CREATE POLICY branches_tenant ON branches USING (true) WITH CHECK (true)",
            owner))
            await tamper.ExecuteNonQueryAsync();
        try
        {
            using var factory = new ApiFactory(_postgres.AppConnectionString);
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("branches_tenant", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("session predicate", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await using var restoreDrop = new NpgsqlCommand("DROP POLICY IF EXISTS branches_tenant ON branches", owner);
            await restoreDrop.ExecuteNonQueryAsync();
            await using var restore = new NpgsqlCommand(
                """
                CREATE POLICY branches_tenant ON branches
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                """,
                owner);
            await restore.ExecuteNonQueryAsync();
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public void Startup_rejects_an_expected_database_name_mismatch()
    {
        var previous = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        try
        {
            using var factory = new ApiFactory(_postgres.AppConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "other_database");
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("does not match", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, previous);
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public async Task Port_8080_listens_on_all_interfaces()
    {
        await AssertListensAsync(8080);
    }

    [Fact]
    public async Task Custom_port_listens_on_the_configured_value()
    {
        await AssertListensAsync(EphemeralPort());
    }

    private static async Task AssertListensAsync(int port)
    {
        using var process = StartApi(port, UnreachableDatabaseUrl());
        try
        {
            var live = await WaitForLiveAsync(process, port);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Contains("live", live.Body, StringComparison.Ordinal);
            Assert.Contains("Healthy", live.Body, StringComparison.Ordinal);
            Assert.True(
                ListensOnAnyAddress(port),
                "Kestrel did not bind 0.0.0.0:" + port.ToString(CultureInfo.InvariantCulture) + ".");
        }
        finally
        {
            Stop(process);
        }
    }

    private static Process StartApi(int port, string databaseUrl)
    {
        var dll = LocateApiDll();
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "exec \"" + dll + "\"",
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["PORT"] = port.ToString(CultureInfo.InvariantCulture);
        start.Environment["DATABASE_URL"] = databaseUrl;
        start.Environment["AUTH_SIGNING_KEY"] = ApiFactory.SigningKey;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment.Remove("ASPNETCORE_URLS");
        start.Environment.Remove("DOTNET_URLS");
        var process = Process.Start(start) ?? throw new InvalidOperationException("The API process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<LiveProbe> WaitForLiveAsync(Process process, int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The API exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (!ListensOnAnyAddress(port))
            {
                await Task.Delay(200);
                continue;
            }

            try
            {
                using var response = await client.GetAsync(
                    "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/health/live");
                return new LiveProbe(response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (HttpRequestException)
            {
                await Task.Delay(200);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException("The API did not accept connections before the deadline.");
    }

    private static void Stop(Process process)
    {
        if (process.HasExited)
            return;
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }

    private static bool ListensOnAnyAddress(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(endpoint =>
            endpoint.Port == port
            && (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any)));
    }

    private static string UnreachableDatabaseUrl() =>
        "Host=127.0.0.1;Port="
        + EphemeralPort().ToString(CultureInfo.InvariantCulture)
        + ";Database=smart_school;Username=smart_school_app;Password=unused;Timeout=2;Ssl Mode=Disable";

    private static int EphemeralPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string LocateApiDll()
    {
        var configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var dll = Path.Combine(FindRepositoryRoot(), "src", "SmartSchool.Api", "bin", configuration, "net10.0", "SmartSchool.Api.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException("SmartSchool.Api.dll was not built.");
        return dll;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SmartSchool.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the Smart School repository.");
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static string RequireScram(string hba)
    {
        var lines = hba.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            if (trimmed.EndsWith("trust", StringComparison.Ordinal))
                lines[index] = lines[index].TrimEnd()[..^"trust".Length] + "scram-sha-256";
        }

        return string.Join('\n', lines);
    }

    private readonly record struct LiveProbe(HttpStatusCode StatusCode, string Body);

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        var pending = new Stack<Exception>();
        pending.Push(error);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
                continue;
            }

            if (current.InnerException is not null)
                pending.Push(current.InnerException);
        }
    }

    private sealed class LoggingApiFactory : ApiFactory
    {
        public LoggingApiFactory(string databaseUrl) : base(databaseUrl)
        {
        }

        public CollectingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new();

        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_gate)
                Messages.Add(message);
        }

        private sealed class Collector(CollectingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.Add(formatter(state, exception));
            }
        }
    }
}
