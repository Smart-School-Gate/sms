using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartSchool.Api.Hosting;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SecurityHardeningTests
{
    private readonly PostgresFixture _postgres;

    public SecurityHardeningTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public void Startup_rejects_a_missing_or_mismatched_expected_database_name()
    {
        var previous = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        try
        {
            using (var factory = new ApiFactory(_postgres.AppConnectionString))
            {
                Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, null);
                var missing = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
                Assert.Contains("EXPECTED_DATABASE_NAME", missing.ToString());
            }

            using var mismatched = new ApiFactory(_postgres.AppConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "other_database");
            var error = Assert.Throws<InvalidOperationException>(() => mismatched.CreateClient());
            Assert.Contains("does not match", error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, previous);
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public async Task Runtime_role_cannot_create_tables()
    {
        await using var app = new NpgsqlConnection(_postgres.AppConnectionString);
        await app.OpenAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var create = new NpgsqlCommand("CREATE TABLE public.security_probe (id integer)", app);
            await create.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);

        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP TABLE IF EXISTS public.security_probe", owner);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Startup_does_not_require_cluster_wide_connect_isolation()
    {
        const string database = "smart_school_side";
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await DropDatabaseAsync(owner, database);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using (var grant = new NpgsqlCommand(
                $"GRANT CONNECT ON DATABASE {database} TO smart_school_app",
                owner))
                await grant.ExecuteNonQueryAsync();

            using var factory = new ApiFactory(_postgres.AppConnectionString);
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health/live");
            Assert.True(response.IsSuccessStatusCode);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var revoke = new NpgsqlCommand(
                $"REVOKE ALL PRIVILEGES ON DATABASE {database} FROM smart_school_app",
                owner);
            await revoke.ExecuteNonQueryAsync();
            await DropDatabaseAsync(owner, database);
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public async Task Startup_rejects_a_policy_whose_expression_was_replaced()
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
            Assert.Contains("branches_tenant", error.ToString());
            Assert.Contains("session predicate", error.ToString());
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
    public async Task Startup_rejects_an_extra_or_tautological_policy()
    {
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var extra = new NpgsqlCommand(
            "CREATE POLICY branches_open ON branches USING (true) WITH CHECK (true)",
            owner))
            await extra.ExecuteNonQueryAsync();
        try
        {
            using var factory = new ApiFactory(_postgres.AppConnectionString);
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("branches_open", error.ToString());
            Assert.Contains("approved set", error.ToString());
        }
        finally
        {
            await using var drop = new NpgsqlCommand("DROP POLICY IF EXISTS branches_open ON branches", owner);
            await drop.ExecuteNonQueryAsync();
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }

        await using (var drop = new NpgsqlCommand("DROP POLICY branches_tenant ON branches", owner))
            await drop.ExecuteNonQueryAsync();
        await using (var tautology = new NpgsqlCommand(
            "CREATE POLICY branches_tenant ON branches USING (school_id = school_id) WITH CHECK (school_id = school_id)",
            owner))
            await tautology.ExecuteNonQueryAsync();
        try
        {
            using var factory = new ApiFactory(_postgres.AppConnectionString);
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("branches_tenant", error.ToString());
            Assert.Contains("session predicate", error.ToString());
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
    public async Task Admin_existence_check_sees_rows_hidden_by_row_level_security()
    {
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var insertUser = new NpgsqlCommand(
            """
            INSERT INTO users (id, username, password_hash, preferred_locale, status, must_change_password, token_version, created_at_utc)
            VALUES (@id, @username, 'not-a-login-hash', 'ar', 'active', false, 1, now())
            """,
            owner))
        {
            insertUser.Parameters.AddWithValue("id", userId);
            insertUser.Parameters.AddWithValue("username", "security.admin." + userId.ToString("N"));
            await insertUser.ExecuteNonQueryAsync();
        }

        await using (var insertMembership = new NpgsqlCommand(
            """
            INSERT INTO user_memberships (id, user_id, school_id, role, status, created_at_utc)
            VALUES (@id, @user, @school, 'school_admin', 'active', now())
            """,
            owner))
        {
            insertMembership.Parameters.AddWithValue("id", membershipId);
            insertMembership.Parameters.AddWithValue("user", userId);
            insertMembership.Parameters.AddWithValue("school", _postgres.Seed.SchoolAId);
            await insertMembership.ExecuteNonQueryAsync();
        }

        try
        {
            await using var app = new NpgsqlConnection(_postgres.AppConnectionString);
            await app.OpenAsync();
            await using (var visible = new NpgsqlCommand("SELECT count(*) FROM user_memberships", app))
                Assert.Equal(0L, (long)(await visible.ExecuteScalarAsync() ?? 0L));
            await using var defined = new NpgsqlCommand("SELECT app_school_admin_exists()", app);
            Assert.Equal(true, await defined.ExecuteScalarAsync());
        }
        finally
        {
            await using var deleteMembership = new NpgsqlCommand("DELETE FROM user_memberships WHERE id = @id", owner);
            deleteMembership.Parameters.AddWithValue("id", membershipId);
            await deleteMembership.ExecuteNonQueryAsync();
            await using var deleteUser = new NpgsqlCommand("DELETE FROM users WHERE id = @id", owner);
            deleteUser.Parameters.AddWithValue("id", userId);
            await deleteUser.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Provision_admin_refuses_a_role_other_than_the_runtime_role()
    {
        const string role = "smart_school_other";
        const string password = "Other-role-password";
        var previousDatabase = Environment.GetEnvironmentVariable("DATABASE_URL");
        var previousExpected = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        var previousSchool = Environment.GetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME");
        var previousUser = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME");
        var previousPassword = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await DropRoleAsync(owner, "smart_school", role);
        await using (var create = new NpgsqlCommand(
            $"CREATE ROLE {role} NOLOGIN NOINHERIT NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION",
            owner))
            await create.ExecuteNonQueryAsync();
        await using (var login = new NpgsqlCommand($"ALTER ROLE {role} LOGIN PASSWORD '{password}'", owner))
            await login.ExecuteNonQueryAsync();
        await using (var grant = new NpgsqlCommand($"GRANT CONNECT ON DATABASE smart_school TO {role}", owner))
            await grant.ExecuteNonQueryAsync();
        try
        {
            var other = new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString)
            {
                Username = role,
                Password = password
            };
            Environment.SetEnvironmentVariable("DATABASE_URL", other.ConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "smart_school");
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", "Other School");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", "other.admin");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", "Other-admin-password");
            Assert.Equal(1, await ProvisionAdminCommand.RunAsync());
            await using var count = new NpgsqlCommand(
                "SELECT count(*) FROM users WHERE username = 'other.admin'",
                owner);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropRoleAsync(owner, "smart_school", role);
            Environment.SetEnvironmentVariable("DATABASE_URL", previousDatabase);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, previousExpected);
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", previousSchool);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", previousUser);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", previousPassword);
        }
    }

    [Fact]
    public async Task Startup_rejects_an_empty_database()
    {
        const string database = "smart_school_plain";
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await DropDatabaseAsync(owner, database);
        await using (var createDatabase = new NpgsqlCommand($"CREATE DATABASE {database}", owner))
            await createDatabase.ExecuteNonQueryAsync();
        await using (var revokeMain = new NpgsqlCommand(
            """
            REVOKE CONNECT ON DATABASE smart_school FROM PUBLIC;
            REVOKE CONNECT ON DATABASE smart_school FROM smart_school_app;
            """,
            owner))
            await revokeMain.ExecuteNonQueryAsync();
        try
        {
            var plainOwner = new NpgsqlConnectionStringBuilder(_postgres.OwnerConnectionString)
            {
                Database = database
            };
            await using (var plain = new NpgsqlConnection(plainOwner.ConnectionString))
            {
                await plain.OpenAsync();
                await using var revokePublic = new NpgsqlCommand(
                    $"REVOKE CONNECT ON DATABASE {database} FROM PUBLIC",
                    plain);
                await revokePublic.ExecuteNonQueryAsync();
                await using var grant = new NpgsqlCommand(
                    $"GRANT CONNECT ON DATABASE {database} TO smart_school_app",
                    plain);
                await grant.ExecuteNonQueryAsync();
                await using var revokeCreate = new NpgsqlCommand(
                    "REVOKE CREATE ON SCHEMA public FROM PUBLIC",
                    plain);
                await revokeCreate.ExecuteNonQueryAsync();
            }

            var app = new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString)
            {
                Database = database
            };
            using var factory = new ApiFactory(app.ConnectionString);
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("users", error.ToString());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var restore = new NpgsqlCommand(
                """
                GRANT CONNECT ON DATABASE smart_school TO smart_school_app;
                GRANT CONNECT ON DATABASE smart_school TO PUBLIC;
                """,
                owner);
            await restore.ExecuteNonQueryAsync();
            await DropDatabaseAsync(owner, database);
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "smart_school");
        }
    }

    [Fact]
    public async Task Provision_admin_refuses_a_privileged_login()
    {
        const string role = "smart_school_bypass";
        const string password = "Bypass-role-password";
        var previousDatabase = Environment.GetEnvironmentVariable("DATABASE_URL");
        var previousExpected = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        var previousSchool = Environment.GetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME");
        var previousUser = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME");
        var previousPassword = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await DropRoleAsync(owner, "smart_school", role);
        await using (var create = new NpgsqlCommand(
            $"CREATE ROLE {role} NOLOGIN NOINHERIT BYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION",
            owner))
            await create.ExecuteNonQueryAsync();
        await using (var login = new NpgsqlCommand($"ALTER ROLE {role} LOGIN PASSWORD '{password}'", owner))
            await login.ExecuteNonQueryAsync();
        await using (var grant = new NpgsqlCommand($"GRANT CONNECT ON DATABASE smart_school TO {role}", owner))
            await grant.ExecuteNonQueryAsync();
        try
        {
            var bypass = new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString)
            {
                Username = role,
                Password = password
            };
            Environment.SetEnvironmentVariable("DATABASE_URL", bypass.ConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "smart_school");
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", "Privileged School");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", "privileged.admin");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", "Privileged-password-123");

            Assert.Equal(1, await ProvisionAdminCommand.RunAsync());

            await using var count = new NpgsqlCommand(
                "SELECT count(*) FROM users WHERE username = 'privileged.admin'",
                owner);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L));

            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.OwnerConnectionString);
            Assert.Equal(1, await ProvisionAdminCommand.RunAsync());
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropRoleAsync(owner, "smart_school", role);
            Environment.SetEnvironmentVariable("DATABASE_URL", previousDatabase);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, previousExpected);
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", previousSchool);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", previousUser);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", previousPassword);
        }
    }

    [Fact]
    public async Task Rollback_drops_the_runtime_role_after_the_tables()
    {
        var migrationPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SmartSchool.Infrastructure",
            "Persistence",
            "Migrations",
            "20261025143006_InitialFoundation.cs");
        var source = await File.ReadAllTextAsync(migrationPath);
        var downAt = source.IndexOf("protected override void Down", StringComparison.Ordinal);
        var down = source[downAt..];
        var roleCall = down.IndexOf("RlsSql.DropRuntimeRole", StringComparison.Ordinal);
        var lastTable = down.LastIndexOf("migrationBuilder.DropTable", StringComparison.Ordinal);
        Assert.True(roleCall > lastTable);
        Assert.DoesNotContain("DROP ROLE", RlsSql.Down, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP ROLE IF EXISTS smart_school_app", RlsSql.DropRuntimeRole, StringComparison.Ordinal);

        await using var cluster = await LocalCluster.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSchoolPostgres(cluster.OwnerConnectionString)
            .Options;
        await using (var db = new AppDbContext(options, new DbSession()))
        {
            await db.Database.MigrateAsync();
            await db.Database.GetService<IMigrator>().MigrateAsync("0");
        }

        await using var connection = new NpgsqlConnection(cluster.OwnerConnectionString);
        await connection.OpenAsync();
        await using (var tables = new NpgsqlCommand("SELECT to_regclass('public.schools') IS NULL", connection))
            Assert.Equal(true, await tables.ExecuteScalarAsync());
        await using var roles = new NpgsqlCommand(
            "SELECT NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'smart_school_app')",
            connection);
        Assert.Equal(true, await roles.ExecuteScalarAsync());
    }

    private static async Task DropDatabaseAsync(NpgsqlConnection owner, string database)
    {
        await using var terminate = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @database AND pid <> pg_backend_pid()
            """,
            owner);
        terminate.Parameters.AddWithValue("database", database);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database}", owner);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task DropRoleAsync(NpgsqlConnection owner, string database, string role)
    {
        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role)", owner);
        exists.Parameters.AddWithValue("role", role);
        if (await exists.ExecuteScalarAsync() is not true)
            return;

        await using var databaseExists = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)",
            owner);
        databaseExists.Parameters.AddWithValue("database", database);
        if (await databaseExists.ExecuteScalarAsync() is true)
        {
            await using var revoke = new NpgsqlCommand(
                $"REVOKE ALL PRIVILEGES ON DATABASE {database} FROM {role}",
                owner);
            await revoke.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", owner);
        await drop.ExecuteNonQueryAsync();
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

    private sealed class LocalCluster : IAsyncDisposable
    {
        private readonly string _binaryDirectory;
        private readonly string _dataDirectory;

        private LocalCluster(string binaryDirectory, string dataDirectory, string ownerConnectionString)
        {
            _binaryDirectory = binaryDirectory;
            _dataDirectory = dataDirectory;
            OwnerConnectionString = ownerConnectionString;
        }

        public string OwnerConnectionString { get; }

        public static async Task<LocalCluster> StartAsync()
        {
            var binaryDirectory = Path.Combine(FindRepositoryRoot(), ".tools", "pgsql", "bin");
            var dataDirectory = Path.Combine(Path.GetTempPath(), "smart-school-rollback-" + Guid.NewGuid().ToString("N"));
            var port = ReservePort();
            Run(binaryDirectory, Path.Combine(binaryDirectory, "initdb.exe"),
                $"-D \"{dataDirectory}\" -U postgres --auth-local=trust --auth-host=trust --encoding=UTF8 --locale=C");
            Run(binaryDirectory, Path.Combine(binaryDirectory, "pg_ctl.exe"),
                $"-D \"{dataDirectory}\" -l \"{Path.Combine(dataDirectory, "server.log")}\" -w start -o \"-p {port} -h 127.0.0.1\"");

            var maintenance = $"Host=127.0.0.1;Port={port};Database=postgres;Username=postgres;Ssl Mode=Disable";
            var owner = $"Host=127.0.0.1;Port={port};Database=smart_school;Username=postgres;Ssl Mode=Disable";
            await using (var connection = new NpgsqlConnection(maintenance))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand("CREATE DATABASE smart_school", connection);
                await create.ExecuteNonQueryAsync();
            }

            return new LocalCluster(binaryDirectory, dataDirectory, owner);
        }

        public async ValueTask DisposeAsync()
        {
            var pgCtl = Path.Combine(_binaryDirectory, "pg_ctl.exe");
            if (File.Exists(pgCtl))
                Run(_binaryDirectory, pgCtl, $"-D \"{_dataDirectory}\" -m fast stop");

            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }

            await Task.CompletedTask;
        }

        private static int ReservePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void Run(string binaryDirectory, string fileName, string arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = binaryDirectory,
                UseShellExecute = false
            };
            start.Environment["PATH"] = binaryDirectory + Path.PathSeparator + start.Environment["PATH"];
            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with exit code {process.ExitCode}.");
        }
    }
}
