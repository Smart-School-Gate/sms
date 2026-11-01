using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Hosting;

public static class ProvisionAdminCommand
{
    public static async Task<int> RunAsync()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        var schoolName = Environment.GetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME");
        var branchName = Environment.GetEnvironmentVariable("BOOTSTRAP_BRANCH_NAME");
        var username = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(databaseUrl)
            || string.IsNullOrWhiteSpace(schoolName)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("DATABASE_URL, BOOTSTRAP_SCHOOL_NAME, BOOTSTRAP_ADMIN_USERNAME, and BOOTSTRAP_ADMIN_PASSWORD are required.");
            return 1;
        }

        if (PostgresConnectionStrings.IsMarketingDatabase(databaseUrl))
        {
            Console.Error.WriteLine("Refusing to provision into the marketing database neondb.");
            return 1;
        }

        if (password.Length < 12 || password.Contains("CHANGE_ME", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("BOOTSTRAP_ADMIN_PASSWORD must be at least 12 characters and must not be a sample value.");
            return 1;
        }

        try
        {
            var session = new DbSession();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSchoolPostgres(PostgresConnectionStrings.ToNpgsql(databaseUrl))
                .AddInterceptors(new TenantCommandInterceptor())
                .Options;
            await using var db = new AppDbContext(options, session);
            using var _ = DbSessionAmbient.Push(session);
            if (!await IsApprovedRuntimeAsync(db))
                return 1;

            var normalized = username.Trim().ToLowerInvariant();
            var adminExists = await AdminExistsAsync(db);
            if (adminExists)
            {
                Console.Error.WriteLine("A school admin membership already exists. The bootstrap command is one-time.");
                return 1;
            }

            if (await db.Users.AnyAsync(x => x.Username == normalized))
            {
                Console.Error.WriteLine("That username is already in use.");
                return 1;
            }

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = normalized,
                FullName = schoolName.Trim(),
                PreferredLocale = "ar",
                Status = UserStatus.Active,
                MustChangePassword = true,
                TokenVersion = 1,
                CreatedAtUtc = now
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);

            var school = new School
            {
                Id = Guid.NewGuid(),
                Name = schoolName.Trim(),
                TimeZoneId = "Africa/Cairo",
                DefaultLocale = "ar",
                IsActive = true,
                CreatedAtUtc = now
            };
            session.SetUser(user.Id);
            session.SetSchool(school.Id);
            db.Schools.Add(school);
            db.Branches.Add(new Branch
            {
                Id = Guid.NewGuid(),
                SchoolId = school.Id,
                Name = string.IsNullOrWhiteSpace(branchName) ? "Main" : branchName.Trim(),
                IsActive = true,
                CreatedAtUtc = now
            });
            db.UserMemberships.Add(new UserMembership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SchoolId = school.Id,
                Role = AppRole.SchoolAdmin,
                Status = MembershipStatus.Active,
                CreatedAtUtc = now
            });
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = user.Id,
                SchoolId = school.Id,
                Role = RoleNames.SchoolAdmin,
                Action = "user.provisioned",
                EntityName = "user",
                EntityId = user.Id,
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
            Console.WriteLine("School admin provisioned. The temporary password must be changed on first use.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Provisioning failed: {ex.GetType().Name}");
            return 1;
        }
    }

    private static async Task<bool> IsApprovedRuntimeAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_database(),
                   current_user = @role,
                   rolsuper,
                   rolbypassrls
            FROM pg_roles
            WHERE rolname = current_user
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "role";
        parameter.Value = DatabaseSecurityCheck.RuntimeRole;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            Console.Error.WriteLine("provision-admin could not verify the database role.");
            return false;
        }

        var expectedDatabase = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        if (string.IsNullOrWhiteSpace(expectedDatabase)
            || !string.Equals(reader.GetString(0), expectedDatabase, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("provision-admin requires EXPECTED_DATABASE_NAME to match the connected database.");
            return false;
        }

        if (reader.GetBoolean(2) || reader.GetBoolean(3))
        {
            Console.Error.WriteLine("provision-admin refuses a superuser or BYPASSRLS login.");
            return false;
        }

        if (!reader.GetBoolean(1))
        {
            Console.Error.WriteLine("provision-admin must use the smart_school_app role.");
            return false;
        }

        return true;
    }

    private static async Task<bool> AdminExistsAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT app_school_admin_exists()";
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }
}
