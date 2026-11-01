using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private string? _dataDirectory;
    private string? _binaryDirectory;

    public string AppConnectionString { get; private set; } = "";
    public string OwnerConnectionString { get; private set; } = "";
    public SchoolSeed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _binaryDirectory = Path.Combine(FindRepositoryRoot(), ".tools", "pgsql", "bin");
        var initdb = Path.Combine(_binaryDirectory, "initdb.exe");
        var pgCtl = Path.Combine(_binaryDirectory, "pg_ctl.exe");
        if (!File.Exists(initdb) || !File.Exists(pgCtl))
            throw new InvalidOperationException("Portable PostgreSQL binaries are not available under .tools/pgsql.");

        _dataDirectory = Path.Combine(Path.GetTempPath(), "smart-school-pg-" + Guid.NewGuid().ToString("N"));
        var port = ReservePort();
        var password = "TestApp-" + Guid.NewGuid().ToString("N");

        Run(initdb, $"-D \"{_dataDirectory}\" -U postgres --auth-local=trust --auth-host=trust --encoding=UTF8 --locale=C", captureOutput: true);
        Run(pgCtl, $"-D \"{_dataDirectory}\" -l \"{Path.Combine(_dataDirectory, "server.log")}\" -w start -o \"-p {port} -h 127.0.0.1\"", captureOutput: false);

        var maintenance = $"Host=127.0.0.1;Port={port};Database=postgres;Username=postgres;Ssl Mode=Disable";
        OwnerConnectionString = $"Host=127.0.0.1;Port={port};Database=smart_school;Username=postgres;Ssl Mode=Disable";
        AppConnectionString = $"Host=127.0.0.1;Port={port};Database=smart_school;Username=smart_school_app;Password={password};Ssl Mode=Disable";

        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE smart_school", connection);
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSchoolPostgres(OwnerConnectionString)
            .Options;
        await using (var db = new AppDbContext(options, new DbSession()))
        {
            await db.Database.MigrateAsync();
        }

        await using (var connection = new NpgsqlConnection(OwnerConnectionString))
        {
            await connection.OpenAsync();
            var literal = password.Replace("'", "''");
            await using var login = new NpgsqlCommand($"ALTER ROLE smart_school_app WITH LOGIN PASSWORD '{literal}'", connection);
            await login.ExecuteNonQueryAsync();
            await using var boundary = new NpgsqlCommand(
                """
                DO $$
                DECLARE
                  db text;
                BEGIN
                  FOR db IN
                    SELECT datname FROM pg_database
                    WHERE datallowconn AND datname <> current_database()
                  LOOP
                    EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', db);
                    EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM smart_school_app', db);
                  END LOOP;
                END
                $$;
                """,
                connection);
            await boundary.ExecuteNonQueryAsync();
        }

        Seed = await SeedAsync();
    }

    public Task DisposeAsync()
    {
        if (_binaryDirectory is null || _dataDirectory is null)
            return Task.CompletedTask;

        var pgCtl = Path.Combine(_binaryDirectory, "pg_ctl.exe");
        if (File.Exists(pgCtl))
            Run(pgCtl, $"-D \"{_dataDirectory}\" -m fast stop", captureOutput: false);

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }

    private async Task<SchoolSeed> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var hasher = new PasswordHasher<User>();
        var schoolA = Guid.NewGuid();
        var schoolB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var yearA = Guid.NewGuid();
        var yearB = Guid.NewGuid();
        var gradeA = Guid.NewGuid();
        var gradeB = Guid.NewGuid();
        var classA = Guid.NewGuid();
        var classB = Guid.NewGuid();
        var subjectA = Guid.NewGuid();
        var subjectB = Guid.NewGuid();
        var studentUser = User(hasher, "student.a", "StudentPass-123", "Student A", now);
        var parentUser = User(hasher, "parent.a", "ParentPass-123", "Parent A", now);
        var otherStudentUser = User(hasher, "student.b", "StudentPass-123", "Student B", now);
        var schoolBStudentUser = User(hasher, "student.c", "StudentPass-123", "Student C", now);
        var temporaryUser = User(hasher, "temp.student", "Temporary-12345", "Temporary Student", now);
        var teacherUser = User(hasher, "teacher.a", "TeacherPass-123", "Teacher A", now);
        var supervisorUser = User(hasher, "supervisor.a", "SupervisorPass-123", "Supervisor A", now);
        temporaryUser.MustChangePassword = true;
        var student = new Student
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolA,
            BranchId = branchA,
            UserId = studentUser.Id,
            StudentCode = "A-100",
            FullName = "Student A",
            DateOfBirth = new DateOnly(2014, 3, 2),
            CreatedAtUtc = now
        };
        var unlinked = new Student
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolA,
            BranchId = branchA,
            UserId = otherStudentUser.Id,
            StudentCode = "A-200",
            FullName = "Student B",
            CreatedAtUtc = now
        };
        var otherSchoolStudent = new Student
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolB,
            BranchId = branchB,
            UserId = schoolBStudentUser.Id,
            StudentCode = "B-100",
            FullName = "Student C",
            CreatedAtUtc = now
        };
        var temporaryStudent = new Student
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolA,
            BranchId = branchA,
            UserId = temporaryUser.Id,
            StudentCode = "A-300",
            FullName = "Temporary Student",
            CreatedAtUtc = now
        };
        var guardian = new Guardian
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolA,
            UserId = parentUser.Id,
            FullName = "Parent A",
            CreatedAtUtc = now
        };
        var presentSession = Guid.NewGuid();
        var absentSession = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSchoolPostgres(OwnerConnectionString)
            .Options;
        await using var db = new AppDbContext(options, new DbSession());
        db.AddRange(
            studentUser, parentUser, otherStudentUser, schoolBStudentUser, temporaryUser, teacherUser, supervisorUser,
            new School { Id = schoolA, Name = "School A", TimeZoneId = "UTC", DefaultLocale = "ar", IsActive = true, CreatedAtUtc = now },
            new School { Id = schoolB, Name = "School B", TimeZoneId = "UTC", DefaultLocale = "ar", IsActive = true, CreatedAtUtc = now },
            new Branch { Id = branchA, SchoolId = schoolA, Name = "Main A", IsActive = true, CreatedAtUtc = now },
            new Branch { Id = branchB, SchoolId = schoolB, Name = "Main B", IsActive = true, CreatedAtUtc = now },
            Membership(studentUser.Id, schoolA, AppRole.Student, now),
            Membership(parentUser.Id, schoolA, AppRole.Parent, now),
            Membership(otherStudentUser.Id, schoolA, AppRole.Student, now),
            Membership(schoolBStudentUser.Id, schoolB, AppRole.Student, now),
            Membership(temporaryUser.Id, schoolA, AppRole.Student, now),
            Membership(teacherUser.Id, schoolA, AppRole.Teacher, now),
            Membership(supervisorUser.Id, schoolA, AppRole.Supervisor, now),
            student, unlinked, otherSchoolStudent, temporaryStudent,
            guardian,
            new GuardianStudent
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolA,
                GuardianId = guardian.Id,
                StudentId = student.Id,
                Relationship = "parent",
                CreatedAtUtc = now
            },
            new AcademicYear
            {
                Id = yearA,
                SchoolId = schoolA,
                Name = "2026",
                StartsOn = today.AddDays(-30),
                EndsOn = today.AddDays(200),
                IsCurrent = true,
                CreatedAtUtc = now
            },
            new GradeLevel { Id = gradeA, SchoolId = schoolA, Name = "Grade 5", SortOrder = 5, CreatedAtUtc = now },
            new ClassSection
            {
                Id = classA,
                SchoolId = schoolA,
                BranchId = branchA,
                GradeLevelId = gradeA,
                AcademicYearId = yearA,
                Name = "5-A",
                CreatedAtUtc = now
            },
            new Subject { Id = subjectA, SchoolId = schoolA, Name = "Mathematics", CreatedAtUtc = now },
            new AcademicYear
            {
                Id = yearB,
                SchoolId = schoolB,
                Name = "2026-B",
                StartsOn = today.AddDays(-30),
                EndsOn = today.AddDays(200),
                IsCurrent = true,
                CreatedAtUtc = now
            },
            new GradeLevel { Id = gradeB, SchoolId = schoolB, Name = "Grade 1", SortOrder = 1, CreatedAtUtc = now },
            new ClassSection
            {
                Id = classB,
                SchoolId = schoolB,
                BranchId = branchB,
                GradeLevelId = gradeB,
                AcademicYearId = yearB,
                Name = "1-B",
                CreatedAtUtc = now
            },
            new Subject { Id = subjectB, SchoolId = schoolB, Name = "Science", CreatedAtUtc = now },
            new TeacherClassAssignment
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolA,
                UserId = teacherUser.Id,
                ClassSectionId = classA,
                SubjectId = subjectA,
                CreatedAtUtc = now
            },
            new TeacherClassAssignment
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolB,
                UserId = teacherUser.Id,
                ClassSectionId = classB,
                SubjectId = subjectB,
                CreatedAtUtc = now
            },
            new Enrollment
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolA,
                StudentId = student.Id,
                ClassSectionId = classA,
                AcademicYearId = yearA,
                CreatedAtUtc = now
            },
            new TimetableEntry
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolA,
                ClassSectionId = classA,
                SubjectId = subjectA,
                DayOfWeek = today.DayOfWeek,
                StartsAt = new TimeOnly(8, 0),
                EndsAt = new TimeOnly(8, 45),
                Room = "A1",
                CreatedAtUtc = now
            },
            new TimetableEntry
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolB,
                ClassSectionId = classB,
                SubjectId = subjectB,
                DayOfWeek = today.DayOfWeek,
                StartsAt = new TimeOnly(9, 0),
                EndsAt = new TimeOnly(9, 45),
                Room = "B1",
                CreatedAtUtc = now
            },
            new AttendanceSession { Id = presentSession, SchoolId = schoolA, ClassSectionId = classA, SessionDate = today, CreatedAtUtc = now },
            new AttendanceSession { Id = absentSession, SchoolId = schoolA, ClassSectionId = classA, SessionDate = today.AddDays(-1), CreatedAtUtc = now },
            new AttendanceRecord { Id = Guid.NewGuid(), SchoolId = schoolA, SessionId = presentSession, StudentId = student.Id, Status = AttendanceMark.Present, CreatedAtUtc = now },
            new AttendanceRecord { Id = Guid.NewGuid(), SchoolId = schoolA, SessionId = absentSession, StudentId = student.Id, Status = AttendanceMark.Absent, CreatedAtUtc = now });
        await db.SaveChangesAsync();

        return new SchoolSeed(
            schoolA,
            schoolB,
            classA,
            classB,
            student.Id,
            unlinked.Id,
            otherSchoolStudent.Id,
            temporaryStudent.Id,
            temporaryUser.Id,
            today);
    }

    private static User User(PasswordHasher<User> hasher, string username, string password, string name, DateTime now)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = name,
            Email = username + "@school.test",
            Phone = "01000000000",
            PreferredLocale = "ar",
            Status = UserStatus.Active,
            TokenVersion = 1,
            CreatedAtUtc = now
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private static UserMembership Membership(Guid userId, Guid schoolId, AppRole role, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        SchoolId = schoolId,
        Role = role,
        Status = MembershipStatus.Active,
        CreatedAtUtc = now
    };

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void Run(string fileName, string arguments, bool captureOutput)
    {
        var start = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = _binaryDirectory,
            UseShellExecute = false,
            RedirectStandardError = captureOutput,
            RedirectStandardOutput = captureOutput
        };
        start.Environment["PATH"] = _binaryDirectory + Path.PathSeparator + start.Environment["PATH"];
        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
        string stdout = "";
        string stderr = "";
        if (captureOutput)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            stdout = stdoutTask.GetAwaiter().GetResult();
            stderr = stderrTask.GetAwaiter().GetResult();
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with exit code {process.ExitCode}. {stderr} {stdout}");
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
}

public sealed record SchoolSeed(
    Guid SchoolAId,
    Guid SchoolBId,
    Guid ClassAId,
    Guid ClassBId,
    Guid StudentAId,
    Guid UnlinkedStudentId,
    Guid OtherSchoolStudentId,
    Guid TemporaryStudentId,
    Guid TemporaryUserId,
    DateOnly Today);

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
