using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SmartSchool.Infrastructure.Persistence;

public sealed class DatabaseSecurityCheck(ILogger<DatabaseSecurityCheck> logger) : IHostedLifecycleService
{
    public const string RuntimeRole = "smart_school_app";
    public const string ExpectedDatabaseNameVariable = "EXPECTED_DATABASE_NAME";

    private static readonly string[] AuthenticationTables = ["users", "refresh_tokens"];

    private static readonly string[][] RequiredPolicies =
    [
        ["schools", "schools_select", "schools_insert", "schools_update", "schools_delete"],
        ["user_memberships", "user_memberships_select", "user_memberships_modify"],
        ["user_contexts", "user_contexts_access"],
        ["audit_events", "audit_events_select", "audit_events_insert"],
        ["branches", "branches_tenant"],
        ["students", "students_tenant"],
        ["guardians", "guardians_tenant"],
        ["guardian_students", "guardian_students_tenant"],
        ["academic_years", "academic_years_tenant"],
        ["terms", "terms_tenant"],
        ["grade_levels", "grade_levels_tenant"],
        ["class_sections", "class_sections_tenant"],
        ["subjects", "subjects_tenant"],
        ["enrollments", "enrollments_tenant"],
        ["teacher_class_assignments", "teacher_class_assignments_tenant"],
        ["timetable_entries", "timetable_entries_tenant"],
        ["attendance_sessions", "attendance_sessions_tenant"],
        ["attendance_records", "attendance_records_tenant"],
        ["homework_assignments", "homework_assignments_tenant"],
        ["homework_submissions", "homework_submissions_tenant"],
        ["school_trips", "school_trips_tenant"]
    ];

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var databaseUrl = PostgresConnectionStrings.RequireFromEnvironment();
            var connectionString = PostgresConnectionStrings.ToNpgsql(databaseUrl);
            var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await VerifyDatabaseNameAsync(connection, cancellationToken);
            await VerifyRoleAsync(connection, cancellationToken);
            await VerifyBoundariesAsync(connection, cancellationToken);
            await VerifyPoliciesAsync(connection, cancellationToken);
            await VerifyAuthFunctionAsync(connection, cancellationToken);
            logger.LogInformation(
                "Database role passed the row-level security privilege check for database {Database}.",
                databaseName);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Database startup check failed with {ExceptionType}", ex.GetType().Name);
            throw new InvalidOperationException("Database startup check failed.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task VerifyDatabaseNameAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var expected = Environment.GetEnvironmentVariable(ExpectedDatabaseNameVariable);
        if (string.IsNullOrWhiteSpace(expected))
        {
            throw new InvalidOperationException(
                "EXPECTED_DATABASE_NAME must be set to this deployment's database name.");
        }

        await using var command = new NpgsqlCommand("SELECT current_database()", connection);
        var actual = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("EXPECTED_DATABASE_NAME does not match the connected database.");
    }

    private static async Task VerifyRoleAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT current_user = @role AS expected_role,
                   rolsuper,
                   rolbypassrls,
                   rolcreaterole,
                   rolcreatedb,
                   rolinherit,
                   rolreplication,
                   (
                     SELECT count(*)
                     FROM pg_auth_members
                     WHERE member = pg_roles.oid
                   ) AS memberships,
                   (
                     SELECT count(*)
                     FROM pg_class
                     JOIN pg_namespace ON pg_namespace.oid = pg_class.relnamespace
                     WHERE pg_namespace.nspname = 'public'
                       AND pg_class.relkind = 'r'
                       AND pg_class.relowner = pg_roles.oid
                   ) AS owned_tables,
                   has_schema_privilege(current_user, 'public', 'CREATE') AS schema_create
            FROM pg_roles
            WHERE rolname = current_user
            """,
            connection);
        command.Parameters.AddWithValue("role", RuntimeRole);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The database role could not be verified.");

        if (reader.GetBoolean(1) || reader.GetBoolean(2))
        {
            throw new InvalidOperationException(
                "DATABASE_URL must use a role without superuser or BYPASSRLS privileges so tenant row-level security applies.");
        }

        if (!reader.GetBoolean(0))
            throw new InvalidOperationException($"DATABASE_URL must use the {RuntimeRole} role.");

        if (reader.GetBoolean(3) || reader.GetBoolean(4) || reader.GetBoolean(5) || reader.GetBoolean(6))
        {
            throw new InvalidOperationException(
                "DATABASE_URL must use a role without CREATEROLE, CREATEDB, INHERIT, or replication privileges.");
        }

        if (reader.GetInt64(7) != 0)
            throw new InvalidOperationException("The runtime role must not be a member of another role.");

        if (reader.GetInt64(8) != 0)
            throw new InvalidOperationException("The runtime role must not own application tables.");

        if (reader.GetBoolean(9))
            throw new InvalidOperationException("The runtime role must not create objects in the public schema.");
    }

    private static async Task VerifyBoundariesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = RequiredPolicies.Select(static row => row[0]).Concat(AuthenticationTables).ToArray();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname = ANY(@tables)
            """,
            connection);
        command.Parameters.AddWithValue("tables", tables);
        var found = new Dictionary<string, (bool Enabled, bool Forced)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            found[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));

        foreach (var table in AuthenticationTables)
        {
            if (!found.TryGetValue(table, out var authentication))
                throw new InvalidOperationException($"The authentication table {table} is missing.");
            if (authentication.Enabled || authentication.Forced)
            {
                throw new InvalidOperationException(
                    $"The authentication table {table} must not use row-level security until a reviewed lookup exists.");
            }
        }

        foreach (var row in RequiredPolicies)
        {
            if (!found.TryGetValue(row[0], out var security) || !security.Enabled || !security.Forced)
                throw new InvalidOperationException($"Row-level security is not forced on {row[0]}.");
        }
    }

    private static async Task VerifyPoliciesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT tablename, policyname, permissive, cmd, qual, with_check
            FROM pg_policies
            WHERE schemaname = 'public'
            """,
            connection);
        var present = new Dictionary<string, PolicyRow>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            present[reader.GetString(0) + "\n" + reader.GetString(1)] = new PolicyRow(
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        var approved = RequiredExpressions.Select(static row => row.Table + "\n" + row.Name).ToHashSet(StringComparer.Ordinal);
        var protectedTables = RequiredPolicies.Select(static row => row[0]).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, policy) in present)
        {
            var table = key[..key.IndexOf('\n', StringComparison.Ordinal)];
            if (AuthenticationTables.Contains(table, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The authentication table {table} must not use row-level security until a reviewed lookup exists.");
            }

            if (protectedTables.Contains(table) && !approved.Contains(key))
            {
                var name = key[(key.IndexOf('\n', StringComparison.Ordinal) + 1)..];
                throw new InvalidOperationException($"Row-level security policy {name} is not part of the approved set.");
            }

            if (protectedTables.Contains(table)
                && !string.Equals(policy.Permissive, "PERMISSIVE", StringComparison.OrdinalIgnoreCase))
            {
                var name = key[(key.IndexOf('\n', StringComparison.Ordinal) + 1)..];
                throw new InvalidOperationException($"Row-level security policy {name} is not permissive in the approved form.");
            }
        }

        foreach (var row in RequiredPolicies)
        {
            for (var index = 1; index < row.Length; index++)
            {
                if (!present.ContainsKey(row[0] + "\n" + row[index]))
                    throw new InvalidOperationException($"Row-level security policy {row[index]} is missing.");
            }
        }

        foreach (var expected in RequiredExpressions)
        {
            if (!present.TryGetValue(expected.Table + "\n" + expected.Name, out var policy))
                throw new InvalidOperationException($"Row-level security policy {expected.Name} is missing.");

            if (!string.Equals(policy.Command, expected.Command, StringComparison.Ordinal)
                || !expected.Accept(policy.UsingExpression, policy.CheckExpression))
            {
                throw new InvalidOperationException(
                    $"Row-level security policy {expected.Name} does not enforce the session predicate.");
            }
        }
    }

    private static string Compact(string expression) =>
        Regex.Replace(expression, @"\s+", "", RegexOptions.CultureInvariant).ToLowerInvariant();

    private static bool IsSessionEquality(string? expression, string column, string setting)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var compact = Compact(expression);
        if (HasBooleanCombinator(compact) || compact.Contains("isnotnull", StringComparison.Ordinal) || HasSelfEquality(compact))
            return false;

        return Regex.IsMatch(
            compact,
            @"^\(*" + column + @"=\(*nullif\(current_setting\('" + setting + @"'(::\w+)?,true\),''(::\w+)?\)\)::uuid\)*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static bool IsUserAndSchoolCheck(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var compact = Compact(expression);
        if (Regex.IsMatch(compact, @"\bor\b", RegexOptions.CultureInvariant)
            || compact.Contains("isnotnull", StringComparison.Ordinal)
            || HasSelfEquality(compact))
            return false;

        return HasSessionCall(compact, "user_id", "app.user_id")
            && HasSessionCall(compact, "school_id", "app.school_id")
            && Regex.Matches(compact, @"\band\b", RegexOptions.CultureInvariant).Count == 1;
    }

    private static bool HasSessionCall(string compact, string column, string setting) =>
        Regex.IsMatch(
            compact,
            column + @"=\(*nullif\(current_setting\('" + setting + @"'",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool IsSchoolSelect(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var compact = Compact(expression);
        return Regex.Matches(compact, @"\bor\b", RegexOptions.CultureInvariant).Count == 1
            && compact.Contains("user_memberships", StringComparison.Ordinal)
            && HasSessionCall(compact, "user_id", "app.user_id")
            && HasSessionCall(compact, "id", "app.school_id")
            && !compact.Contains("isnotnull", StringComparison.Ordinal)
            && !HasSelfEquality(compact)
            && !ContainsBooleanTrue(expression);
    }

    private static bool IsAuditInsert(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var compact = Compact(expression);
        return Regex.Matches(compact, @"\bor\b", RegexOptions.CultureInvariant).Count == 1
            && compact.Contains("school_idisnull", StringComparison.Ordinal)
            && HasSessionCall(compact, "school_id", "app.school_id")
            && !compact.Contains("isnotnull", StringComparison.Ordinal)
            && !HasSelfEquality(compact)
            && !ContainsBooleanTrue(expression);
    }

    private static bool HasBooleanCombinator(string compact) =>
        Regex.IsMatch(compact, @"\bor\b|\band\b", RegexOptions.CultureInvariant);

    private static bool HasSelfEquality(string compact) =>
        Regex.IsMatch(compact, @"\b([a-z_][a-z0-9_]*)=\1\b", RegexOptions.CultureInvariant);

    private static bool ContainsBooleanTrue(string expression)
    {
        var withoutSettings = Regex.Replace(
            expression,
            @"current_setting\([^)]*\)",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.IsMatch(withoutSettings, @"\btrue\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record PolicyRow(string Permissive, string Command, string? UsingExpression, string? CheckExpression);

    private readonly record struct PolicyExpression(
        string Table,
        string Name,
        string Command,
        Func<string?, string?, bool> Accept);

    private static readonly PolicyExpression[] RequiredExpressions = CreateRequiredExpressions();

    private static PolicyExpression[] CreateRequiredExpressions()
    {
        var policies = new List<PolicyExpression>
        {
            new("schools", "schools_select", "SELECT", (usingExpression, checkExpression) =>
                IsSchoolSelect(usingExpression) && string.IsNullOrWhiteSpace(checkExpression)),
            new("schools", "schools_insert", "INSERT", (usingExpression, checkExpression) =>
                string.IsNullOrWhiteSpace(usingExpression) && IsSessionEquality(checkExpression, "id", "app.school_id")),
            new("schools", "schools_update", "UPDATE", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "id", "app.school_id") && IsSessionEquality(checkExpression, "id", "app.school_id")),
            new("schools", "schools_delete", "DELETE", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "id", "app.school_id") && string.IsNullOrWhiteSpace(checkExpression)),
            new("user_memberships", "user_memberships_select", "SELECT", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "user_id", "app.user_id") && string.IsNullOrWhiteSpace(checkExpression)),
            new("user_memberships", "user_memberships_modify", "ALL", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "user_id", "app.user_id") && IsUserAndSchoolCheck(checkExpression)),
            new("user_contexts", "user_contexts_access", "ALL", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "user_id", "app.user_id") && IsSessionEquality(checkExpression, "user_id", "app.user_id")),
            new("audit_events", "audit_events_select", "SELECT", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "school_id", "app.school_id") && string.IsNullOrWhiteSpace(checkExpression)),
            new("audit_events", "audit_events_insert", "INSERT", (usingExpression, checkExpression) =>
                string.IsNullOrWhiteSpace(usingExpression) && IsAuditInsert(checkExpression))
        };

        foreach (var row in RequiredPolicies)
        {
            if (!row[1].EndsWith("_tenant", StringComparison.Ordinal))
                continue;
            policies.Add(new PolicyExpression(row[0], row[1], "ALL", (usingExpression, checkExpression) =>
                IsSessionEquality(usingExpression, "school_id", "app.school_id")
                && IsSessionEquality(checkExpression, "school_id", "app.school_id")));
        }

        return policies.ToArray();
    }

    private static async Task VerifyAuthFunctionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT p.prosecdef,
                   pg_get_userbyid(p.proowner) = current_user AS owned_by_runtime,
                   p.proconfig,
                   p.prosrc,
                   p.proacl IS NULL OR EXISTS (
                     SELECT 1
                     FROM aclexplode(p.proacl) AS acl
                     WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'
                   ) AS public_execute
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public' AND p.proname = 'app_school_admin_exists'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The function app_school_admin_exists is missing.");

        if (!reader.GetBoolean(0) || reader.GetBoolean(1))
            throw new InvalidOperationException("app_school_admin_exists must be a security-definer function owned by the migration role.");

        var config = reader.IsDBNull(2) ? [] : reader.GetFieldValue<string[]>(2);
        if (config.Length != 1 || !string.Equals(config[0], "search_path=public", StringComparison.Ordinal))
            throw new InvalidOperationException("app_school_admin_exists must set a fixed search_path.");

        var body = reader.IsDBNull(3) ? "" : reader.GetString(3);
        var compactBody = Compact(body);
        if (!compactBody.Contains("fromuser_memberships", StringComparison.Ordinal)
            || !compactBody.Contains("role='school_admin'", StringComparison.Ordinal)
            || !compactBody.Contains("status='active'", StringComparison.Ordinal)
            || compactBody.Contains("execute", StringComparison.Ordinal)
            || compactBody.Contains("||", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("app_school_admin_exists has an unexpected function body.");
        }

        if (reader.GetBoolean(4))
            throw new InvalidOperationException("app_school_admin_exists must not be executable by PUBLIC.");
    }
}
