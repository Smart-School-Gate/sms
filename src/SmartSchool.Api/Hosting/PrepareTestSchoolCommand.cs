using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Hosting;

public static class PrepareTestSchoolCommand
{
    public const string TeacherUsername = "teacher.one";
    public const string SupervisorUsername = "supervisor.one";
    const string TeacherPasswordVariable = "TEST_TEACHER_PASSWORD";
    const string SupervisorPasswordVariable = "TEST_SUPERVISOR_PASSWORD";
    const string YearName = "2026-2027";
    const string GradeName = "الصف الأول";
    const string ClassName = "أولى A";
    const string Arabic = "لغة عربية";
    const string Mathematics = "رياضيات";

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
            Console.Error.WriteLine("Refusing to prepare the marketing database neondb.");
            return 1;
        }

        var databaseName = PostgresConnectionStrings.DatabaseName(migrationUrl);
        if (string.IsNullOrWhiteSpace(databaseName)
            || string.Equals(databaseName, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("MIGRATION_DATABASE_URL must name the school's own database.");
            return 1;
        }

        var expectedDatabase = Environment.GetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable);
        if (!string.Equals(databaseName, expectedDatabase, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Refusing to prepare because the database name does not match EXPECTED_DATABASE_NAME.");
            return 1;
        }

        var envPath = FindEnvFile();
        if (envPath is null)
        {
            Console.Error.WriteLine("The local .env file was not found. Refusing to create accounts without a place to store passwords.");
            return 1;
        }

        try
        {
            var connectionString = PostgresConnectionStrings.ToNpgsql(migrationUrl);
            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using var current = new NpgsqlCommand("SELECT current_database()", verify);
            var actual = (string?)await current.ExecuteScalarAsync();
            if (!string.Equals(actual, expectedDatabase, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Refusing to prepare because the connected database does not match EXPECTED_DATABASE_NAME.");
                return 1;
            }

            var before = await CountsAsync(verify);
            var session = new DbSession();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSchoolPostgres(connectionString)
                .Options;
            await using var db = new AppDbContext(options, session);
            await using var transaction = await db.Database.BeginTransactionAsync();

            var schools = await db.Schools.AsNoTracking().ToListAsync();
            if (schools.Count != 1)
            {
                Console.Error.WriteLine("Expected exactly one school. Refusing to create another school.");
                return 1;
            }

            var school = schools[0];
            session.SetSchool(school.Id);
            var student = await db.Students.FirstOrDefaultAsync(row => row.StudentCode == "S-1001");
            if (student is null)
            {
                Console.Error.WriteLine("The existing student S-1001 was not found. Refusing to create a student.");
                return 1;
            }

            var linked = await db.GuardianStudents.AnyAsync(row => row.StudentId == student.Id && row.Relationship == "parent");
            if (!linked)
            {
                Console.Error.WriteLine("The existing student has no parent link. Refusing to create a parent.");
                return 1;
            }

            var now = DateTime.UtcNow;
            var today = Today(school.TimeZoneId);
            var year = await db.AcademicYears.FirstOrDefaultAsync(row => row.IsCurrent)
                ?? await db.AcademicYears.FirstOrDefaultAsync(row => row.Name == YearName);
            if (year is null)
            {
                year = new AcademicYear
                {
                    Id = Guid.NewGuid(),
                    SchoolId = school.Id,
                    Name = YearName,
                    StartsOn = new DateOnly(2026, 9, 1),
                    EndsOn = new DateOnly(2027, 6, 30),
                    IsCurrent = true,
                    CreatedAtUtc = now
                };
                db.AcademicYears.Add(year);
            }

            var grade = await db.GradeLevels.FirstOrDefaultAsync(row => row.Name == GradeName);
            if (grade is null)
            {
                grade = new GradeLevel
                {
                    Id = Guid.NewGuid(),
                    SchoolId = school.Id,
                    Name = GradeName,
                    SortOrder = 1,
                    CreatedAtUtc = now
                };
                db.GradeLevels.Add(grade);
            }

            var section = await db.ClassSections.FirstOrDefaultAsync(row => row.Name == ClassName && row.AcademicYearId == year.Id);
            if (section is null)
            {
                section = new ClassSection
                {
                    Id = Guid.NewGuid(),
                    SchoolId = school.Id,
                    BranchId = student.BranchId,
                    GradeLevelId = grade.Id,
                    AcademicYearId = year.Id,
                    Name = ClassName,
                    CreatedAtUtc = now
                };
                db.ClassSections.Add(section);
            }

            var enrollment = await db.Enrollments.FirstOrDefaultAsync(row => row.StudentId == student.Id && row.AcademicYearId == year.Id);
            if (enrollment is null)
            {
                db.Enrollments.Add(new Enrollment
                {
                    Id = Guid.NewGuid(),
                    SchoolId = school.Id,
                    StudentId = student.Id,
                    ClassSectionId = section.Id,
                    AcademicYearId = year.Id,
                    CreatedAtUtc = now
                });
            }
            else if (enrollment.ClassSectionId != section.Id)
            {
                section = await db.ClassSections.FirstAsync(row => row.Id == enrollment.ClassSectionId);
            }

            var arabic = await SubjectAsync(db, school.Id, Arabic, now);
            var mathematics = await SubjectAsync(db, school.Id, Mathematics, now);
            var teacher = await AccountAsync(db, school.Id, TeacherUsername, "معلم الاختبار", AppRole.Teacher, TeacherPasswordVariable, envPath, now);
            var supervisor = await AccountAsync(db, school.Id, SupervisorUsername, "مشرف الاختبار", AppRole.Supervisor, SupervisorPasswordVariable, envPath, now);
            if (teacher is null || supervisor is null)
                return 1;

            await AssignmentAsync(db, school.Id, teacher.Id, section.Id, arabic.Id, now);
            await AssignmentAsync(db, school.Id, teacher.Id, section.Id, mathematics.Id, now);
            await LessonAsync(db, school.Id, section.Id, arabic.Id, DayOfWeek.Sunday, new TimeOnly(8, 0), new TimeOnly(8, 45), now);
            await LessonAsync(db, school.Id, section.Id, mathematics.Id, DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(9, 45), now);
            await LessonAsync(db, school.Id, section.Id, mathematics.Id, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 45), now);
            await LessonAsync(db, school.Id, section.Id, arabic.Id, DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(8, 45), now);
            await LessonAsync(db, school.Id, section.Id, mathematics.Id, DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(8, 45), now);
            await LessonAsync(db, school.Id, section.Id, arabic.Id, DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(8, 45), now);
            await FillSchoolDaysAsync(db, school.Id, section.Id, student.Id, year.StartsOn, today, now);
            await FillContactAsync(db, student.UserId, "student.s1001@smartschool.local", "01010000003");
            var guardianId = await db.GuardianStudents.AsNoTracking()
                .Where(row => row.StudentId == student.Id)
                .Select(row => row.GuardianId)
                .FirstOrDefaultAsync();
            if (guardianId != Guid.Empty)
            {
                var guardianUserId = await db.Guardians.AsNoTracking()
                    .Where(row => row.Id == guardianId)
                    .Select(row => row.UserId)
                    .FirstOrDefaultAsync();
                await FillContactAsync(db, guardianUserId, "parent.s1001@smartschool.local", "01010000004");
            }

            var reading = await HomeworkAsync(
                db, school.Id, section.Id, arabic.Id, teacher.Id,
                "قراءة الوحدة الأولى",
                "اقرأ الدرس الأول وأجب عن أسئلة الفهم في كراسة النشاط.",
                "سلّم الإجابة قبل نهاية اليوم المحدد.",
                today.AddDays(3), now);
            var addition = await HomeworkAsync(
                db, school.Id, section.Id, mathematics.Id, teacher.Id,
                "تمارين الجمع",
                "حل تمارين الجمع من صفحة 18 إلى صفحة 20.",
                "اكتب الخطوات كاملة.",
                today.AddDays(5), now);
            var spelling = await HomeworkAsync(
                db, school.Id, section.Id, arabic.Id, teacher.Id,
                "إملاء الكلمات",
                "اكتب كلمات الإملاء التي تم تدريب الفصل عليها.",
                "راجع الكتابة قبل التسليم.",
                today.AddDays(1), now);
            var shapes = await HomeworkAsync(
                db, school.Id, section.Id, mathematics.Id, teacher.Id,
                "مراجعة الأشكال",
                "راجع أشكال الدائرة والمربع والمثلث.",
                "ارسم كل شكل وسمّه.",
                today.AddDays(7), now);
            await SubmissionAsync(db, school.Id, spelling.Id, student.Id, "submitted", "تم حل الإملاء في الكراسة.", now);
            await SubmissionAsync(db, school.Id, reading.Id, student.Id, "reviewed", "تمت مراجعة القراءة.", now);

            await TripAsync(db, school.Id, "رحلة المتحف", "المتحف المصري", 28, "مشرف الاختبار", today.AddDays(2), "current", now);
            await TripAsync(db, school.Id, "رحلة الحديقة", "حديقة الأزهر", 22, "مشرف الاختبار", today.AddDays(-10), "completed", now);
            await TripAsync(db, school.Id, "رحلة المكتبة", "مكتبة المدرسة", 18, "مشرف الاختبار", today.AddDays(6), "current", now);
            await TripAsync(db, school.Id, "رحلة العلوم", "معمل العلوم", 30, "مشرف الاختبار", today.AddDays(-3), "completed", now);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var after = await CountsAsync(verify);
            Console.WriteLine($"Prepared test data in database {actual}.");
            Console.WriteLine("Reused the existing school, student S-1001, and linked parent.");
            Console.WriteLine($"Teacher account {TeacherUsername}: {(teacher.Created ? "created" : "reused")}.");
            Console.WriteLine($"Supervisor account {SupervisorUsername}: {(supervisor.Created ? "created" : "reused")}.");
            Console.WriteLine($"Class {section.Name}. Subjects {Arabic}, {Mathematics}.");
            PrintCounts("before", before);
            PrintCounts("after", after);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Test school preparation failed: {ex.GetType().Name}");
            return 1;
        }
    }

    static async Task<Subject> SubjectAsync(AppDbContext db, Guid schoolId, string name, DateTime now)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(row => row.Name == name);
        if (subject is not null)
            return subject;

        subject = new Subject
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            Name = name,
            CreatedAtUtc = now
        };
        db.Subjects.Add(subject);
        return subject;
    }

    static async Task<Account?> AccountAsync(
        AppDbContext db,
        Guid schoolId,
        string username,
        string fullName,
        AppRole role,
        string passwordVariable,
        string envPath,
        DateTime now)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Username == username);
        var created = false;
        if (user is null)
        {
            var password = Environment.GetEnvironmentVariable(passwordVariable);
            if (string.IsNullOrWhiteSpace(password))
            {
                password = CreatePassword();
                StoreSecret(envPath, passwordVariable, password);
            }

            user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                FullName = fullName,
                PreferredLocale = "ar",
                Status = UserStatus.Active,
                MustChangePassword = false,
                TokenVersion = 1,
                CreatedAtUtc = now
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);
            created = true;
        }

        if (string.IsNullOrWhiteSpace(user.FullName))
            user.FullName = fullName;
        if (string.IsNullOrWhiteSpace(user.Email))
            user.Email = username.Replace(".", "", StringComparison.Ordinal) + "@smartschool.local";
        if (string.IsNullOrWhiteSpace(user.Phone))
            user.Phone = role == AppRole.Teacher ? "01010000001" : "01010000002";

        var membership = await db.UserMemberships.FirstOrDefaultAsync(row =>
            row.UserId == user.Id && row.SchoolId == schoolId && row.Role == role);
        if (membership is null)
        {
            db.UserMemberships.Add(new UserMembership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SchoolId = schoolId,
                Role = role,
                Status = MembershipStatus.Active,
                CreatedAtUtc = now
            });
        }
        else if (membership.Status != MembershipStatus.Active)
        {
            Console.Error.WriteLine("An existing membership for this test account is not active. Refusing to change it.");
            return null;
        }

        return new Account(user.Id, created);
    }

    static async Task AssignmentAsync(
        AppDbContext db,
        Guid schoolId,
        Guid userId,
        Guid classSectionId,
        Guid subjectId,
        DateTime now)
    {
        var exists = await db.TeacherClassAssignments.AnyAsync(row =>
            row.UserId == userId && row.ClassSectionId == classSectionId && row.SubjectId == subjectId);
        if (exists)
            return;

        db.TeacherClassAssignments.Add(new TeacherClassAssignment
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            UserId = userId,
            ClassSectionId = classSectionId,
            SubjectId = subjectId,
            CreatedAtUtc = now
        });
    }

    static async Task LessonAsync(
        AppDbContext db,
        Guid schoolId,
        Guid classSectionId,
        Guid subjectId,
        DayOfWeek day,
        TimeOnly startsAt,
        TimeOnly endsAt,
        DateTime now)
    {
        var exists = await db.TimetableEntries.AnyAsync(row =>
            row.ClassSectionId == classSectionId && row.DayOfWeek == day && row.StartsAt == startsAt);
        if (exists)
            return;

        db.TimetableEntries.Add(new TimetableEntry
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            ClassSectionId = classSectionId,
            SubjectId = subjectId,
            DayOfWeek = day,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Room = "A1",
            CreatedAtUtc = now
        });
    }

    static async Task FillSchoolDaysAsync(
        AppDbContext db,
        Guid schoolId,
        Guid classSectionId,
        Guid studentId,
        DateOnly from,
        DateOnly today,
        DateTime now)
    {
        for (var date = from; date <= today; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                continue;

            var status = date.DayNumber % 11 == 0
                ? AttendanceMark.Absent
                : date.DayNumber % 7 == 0
                    ? AttendanceMark.Late
                    : AttendanceMark.Present;
            await MarkAsync(db, schoolId, classSectionId, studentId, date, status, now);
        }
    }

    static async Task FillContactAsync(AppDbContext db, Guid? userId, string email, string phone)
    {
        if (userId is not Guid id || id == Guid.Empty)
            return;

        var user = await db.Users.FirstOrDefaultAsync(row => row.Id == id);
        if (user is null)
            return;

        if (string.IsNullOrWhiteSpace(user.Email))
            user.Email = email;
        if (string.IsNullOrWhiteSpace(user.Phone))
            user.Phone = phone;
    }

    static async Task<HomeworkAssignment> HomeworkAsync(
        AppDbContext db,
        Guid schoolId,
        Guid classSectionId,
        Guid subjectId,
        Guid teacherUserId,
        string title,
        string description,
        string instructions,
        DateOnly dueOn,
        DateTime now)
    {
        var existing = await db.HomeworkAssignments.FirstOrDefaultAsync(row =>
            row.ClassSectionId == classSectionId && row.Title == title);
        if (existing is not null)
        {
            existing.DueOn = dueOn;
            existing.Description = description;
            existing.Instructions = instructions;
            existing.SubjectId = subjectId;
            existing.TeacherUserId = teacherUserId;
            return existing;
        }

        var created = new HomeworkAssignment
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            ClassSectionId = classSectionId,
            SubjectId = subjectId,
            TeacherUserId = teacherUserId,
            Title = title,
            Description = description,
            Instructions = instructions,
            DueOn = dueOn,
            CreatedAtUtc = now
        };
        db.HomeworkAssignments.Add(created);
        return created;
    }

    static async Task SubmissionAsync(
        AppDbContext db,
        Guid schoolId,
        Guid assignmentId,
        Guid studentId,
        string status,
        string note,
        DateTime now)
    {
        var existing = await db.HomeworkSubmissions.FirstOrDefaultAsync(row =>
            row.AssignmentId == assignmentId && row.StudentId == studentId);
        if (existing is not null)
        {
            existing.Status = status;
            existing.Note = note;
            return;
        }

        db.HomeworkSubmissions.Add(new HomeworkSubmission
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            Status = status,
            Note = note,
            SubmittedAtUtc = now
        });
    }

    static async Task TripAsync(
        AppDbContext db,
        Guid schoolId,
        string title,
        string subtitle,
        int studentCount,
        string supervisorName,
        DateOnly tripOn,
        string status,
        DateTime now)
    {
        var existing = await db.SchoolTrips.FirstOrDefaultAsync(row => row.Title == title);
        if (existing is not null)
        {
            existing.Subtitle = subtitle;
            existing.StudentCount = studentCount;
            existing.SupervisorName = supervisorName;
            existing.TripOn = tripOn;
            existing.Status = status;
            return;
        }

        db.SchoolTrips.Add(new SchoolTrip
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            Title = title,
            Subtitle = subtitle,
            StudentCount = studentCount,
            SupervisorName = supervisorName,
            TripOn = tripOn,
            Status = status,
            CreatedAtUtc = now
        });
    }

    static async Task MarkAsync(
        AppDbContext db,
        Guid schoolId,
        Guid classSectionId,
        Guid studentId,
        DateOnly date,
        AttendanceMark status,
        DateTime now)
    {
        var session = await db.AttendanceSessions.FirstOrDefaultAsync(row =>
            row.ClassSectionId == classSectionId && row.SessionDate == date);
        if (session is null)
        {
            session = new AttendanceSession
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolId,
                ClassSectionId = classSectionId,
                SessionDate = date,
                CreatedAtUtc = now
            };
            db.AttendanceSessions.Add(session);
        }

        var marked = await db.AttendanceRecords.AnyAsync(row => row.SessionId == session.Id && row.StudentId == studentId);
        if (marked)
            return;

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            SessionId = session.Id,
            StudentId = studentId,
            Status = status,
            CreatedAtUtc = now
        });
    }

    static DateOnly Today(string timeZoneId)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    static async Task<Dictionary<string, long>> CountsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT 'users', count(*) FROM users
            UNION ALL SELECT 'user_memberships', count(*) FROM user_memberships
            UNION ALL SELECT 'students', count(*) FROM students
            UNION ALL SELECT 'guardians', count(*) FROM guardians
            UNION ALL SELECT 'guardian_students', count(*) FROM guardian_students
            UNION ALL SELECT 'academic_years', count(*) FROM academic_years
            UNION ALL SELECT 'grade_levels', count(*) FROM grade_levels
            UNION ALL SELECT 'class_sections', count(*) FROM class_sections
            UNION ALL SELECT 'subjects', count(*) FROM subjects
            UNION ALL SELECT 'enrollments', count(*) FROM enrollments
            UNION ALL SELECT 'teacher_class_assignments', count(*) FROM teacher_class_assignments
            UNION ALL SELECT 'timetable_entries', count(*) FROM timetable_entries
            UNION ALL SELECT 'attendance_sessions', count(*) FROM attendance_sessions
            UNION ALL SELECT 'attendance_records', count(*) FROM attendance_records
            UNION ALL SELECT 'homework_assignments', count(*) FROM homework_assignments
            UNION ALL SELECT 'homework_submissions', count(*) FROM homework_submissions
            UNION ALL SELECT 'school_trips', count(*) FROM school_trips
            ORDER BY 1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<string, long>();
        while (await reader.ReadAsync())
            counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    static void PrintCounts(string label, Dictionary<string, long> counts)
    {
        foreach (var row in counts)
            Console.WriteLine($"{label} {row.Key}={row.Value}");
    }

    static string CreatePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(24);
        return string.Create(bytes.Length, bytes, (chars, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                chars[i] = alphabet[source[i] % alphabet.Length];
        });
    }

    static string? FindEnvFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, ".env");
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
        }

        return null;
    }

    static void StoreSecret(string path, string key, string value)
    {
        var text = File.ReadAllText(path);
        var exists = text.Split('\n').Any(line => line.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
        if (exists)
            return;
        if (text.Length > 0 && !text.EndsWith('\n'))
            text += "\n";
        File.WriteAllText(path, text + key + "=" + value + "\n");
    }

    sealed record Account(Guid Id, bool Created);
}
