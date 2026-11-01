using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SmartSchool.Api.Hosting;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class Phase1WorkflowTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Phase1WorkflowTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.AppConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Student_reads_own_profile_home_attendance_and_timetable()
    {
        var token = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var profile = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/profile", token);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var profileBody = await ReadAsync(profile);
        Assert.Equal("Student A", profileBody.GetProperty("fullName").GetString());
        Assert.Equal("A-100", profileBody.GetProperty("studentCode").GetString());
        Assert.Equal("School A", profileBody.GetProperty("school").GetProperty("name").GetString());
        Assert.Equal("5-A", profileBody.GetProperty("classSection").GetProperty("name").GetString());

        using var home = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/home", token);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var homeBody = await ReadAsync(home);
        Assert.Equal(0.5m, homeBody.GetProperty("attendance").GetProperty("fraction").GetDecimal());
        Assert.Equal(JsonValueKind.Array, homeBody.GetProperty("deadlines").ValueKind);
        Assert.All(homeBody.GetProperty("deadlines").EnumerateArray(), deadline =>
        {
            Assert.Equal("homework", deadline.GetProperty("kind").GetString());
            Assert.False(string.IsNullOrWhiteSpace(deadline.GetProperty("title").GetString()));
        });
        Assert.Equal("Mathematics", homeBody.GetProperty("timetable")[0].GetProperty("subjectName").GetString());
        Assert.Equal("08:00", homeBody.GetProperty("timetable")[0].GetProperty("startsAt").GetString());

        using var attendance = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/attendance", token);
        var attendanceBody = await ReadAsync(attendance);
        Assert.Equal(1, attendanceBody.GetProperty("presentCount").GetInt32());
        Assert.Equal(1, attendanceBody.GetProperty("absentCount").GetInt32());
        Assert.Equal(0.5m, attendanceBody.GetProperty("fraction").GetDecimal());

        using var timetable = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/timetable", token);
        var timetableBody = await ReadAsync(timetable);
        Assert.Equal("Mathematics", timetableBody.GetProperty("entries")[0].GetProperty("subjectName").GetString());
    }

    [Fact]
    public async Task Parent_reads_linked_child_and_cannot_read_unlinked_or_other_school_students()
    {
        var deniedContext = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "parent",
            studentId = _postgres.Seed.UnlinkedStudentId
        }, await AccessTokenAsync("parent.a", "ParentPass-123", "parent")));
        Assert.Equal(HttpStatusCode.Forbidden, deniedContext.StatusCode);
        Assert.Equal(ProblemResponses.Forbidden, (await ReadAsync(deniedContext)).GetProperty("type").GetString());

        var token = await LoginAndSelectAsync("parent.a", "ParentPass-123", "parent", _postgres.Seed.StudentAId);
        using var linked = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/profile", token);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal("Student A", (await ReadAsync(linked)).GetProperty("fullName").GetString());

        using var unlinked = await GetAsync(
            $"/api/v1/students/{_postgres.Seed.UnlinkedStudentId}/profile",
            token,
            schoolHeader: _postgres.Seed.SchoolBId);
        Assert.Equal(HttpStatusCode.NotFound, unlinked.StatusCode);
        var unlinkedBody = await unlinked.Content.ReadAsStringAsync();
        Assert.Contains(ProblemResponses.NotFound, unlinkedBody);
        Assert.DoesNotContain("Student B", unlinkedBody);

        using var otherSchool = await GetAsync($"/api/v1/students/{_postgres.Seed.OtherSchoolStudentId}/home", token);
        Assert.Equal(HttpStatusCode.NotFound, otherSchool.StatusCode);
        Assert.DoesNotContain("Student C", await otherSchool.Content.ReadAsStringAsync());

        using var timetable = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/timetable", token);
        Assert.Equal(HttpStatusCode.OK, timetable.StatusCode);
        Assert.Equal("Mathematics", (await ReadAsync(timetable)).GetProperty("entries")[0].GetProperty("subjectName").GetString());

        using var teacherSchedule = await GetAsync("/api/v1/teacher/schedule", token);
        Assert.Equal(HttpStatusCode.Forbidden, teacherSchedule.StatusCode);
    }

    [Fact]
    public async Task Teacher_home_is_limited_to_the_authorized_teacher_context()
    {
        using var missing = await _client.GetAsync("/api/v1/teacher/schedule");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var access = await AccessTokenAsync("teacher.a", "TeacherPass-123", "teacher");
        using var otherSchool = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolBId,
            role = "teacher"
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, otherSchool.StatusCode);

        using var withStudent = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "teacher",
            studentId = _postgres.Seed.StudentAId
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, withStudent.StatusCode);

        using var selected = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "teacher"
        }, access));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var token = (await ReadAsync(selected)).GetProperty("accessToken").GetString()!;

        using var home = await GetAsync("/api/v1/teacher/home", token);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var body = await ReadAsync(home);
        Assert.Equal("Teacher A", body.GetProperty("fullName").GetString());
        Assert.Equal(1, body.GetProperty("todayLessonCount").GetInt32());
        Assert.Equal(1, body.GetProperty("todayLessonTotal").GetInt32());
        var lesson = body.GetProperty("lessons").EnumerateArray().Single();
        Assert.Equal("Mathematics", lesson.GetProperty("title").GetString());
        Assert.Equal("5-A", lesson.GetProperty("className").GetString());
        Assert.Equal("08:00", lesson.GetProperty("startsAt").GetString());
        Assert.DoesNotContain("Science", body.ToString());
        Assert.DoesNotContain("Student A", body.ToString());
        Assert.DoesNotContain("1-B", body.ToString());

        using var studentProfile = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/profile", token);
        Assert.Equal(HttpStatusCode.Forbidden, studentProfile.StatusCode);
        Assert.DoesNotContain("Student A", await studentProfile.Content.ReadAsStringAsync());

        var studentToken = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var deniedHome = await GetAsync("/api/v1/teacher/home", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedHome.StatusCode);
        Assert.DoesNotContain("Teacher A", await deniedHome.Content.ReadAsStringAsync());

        using var schedule = await GetAsync("/api/v1/teacher/schedule", token);
        Assert.Equal(HttpStatusCode.OK, schedule.StatusCode);
        var scheduleBody = await ReadAsync(schedule);
        Assert.Equal("Mathematics", scheduleBody.GetProperty("lessons")[0].GetProperty("title").GetString());
        Assert.DoesNotContain("Science", scheduleBody.ToString());

        using var studentSchedule = await GetAsync("/api/v1/teacher/schedule", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, studentSchedule.StatusCode);
    }

    [Fact]
    public async Task Assignment_and_timetable_constraints_reject_duplicates_and_cross_school_links()
    {
        await using var connection = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await connection.OpenAsync();

        await using var duplicate = new NpgsqlCommand("""
            INSERT INTO teacher_class_assignments (id, school_id, user_id, class_section_id, subject_id, created_at_utc)
            SELECT gen_random_uuid(), school_id, user_id, class_section_id, subject_id, created_at_utc
            FROM teacher_class_assignments
            WHERE school_id = (SELECT id FROM schools WHERE name = 'School A')
            LIMIT 1
            """, connection);
        var duplicateError = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal("23505", duplicateError.SqlState);

        await using var crossed = new NpgsqlCommand("""
            INSERT INTO teacher_class_assignments (id, school_id, user_id, class_section_id, subject_id, created_at_utc)
            SELECT gen_random_uuid(),
                   (SELECT id FROM schools WHERE name = 'School A'),
                   user_id,
                   (SELECT id FROM class_sections WHERE name = '1-B'),
                   (SELECT id FROM subjects WHERE name = 'Mathematics'),
                   now()
            FROM teacher_class_assignments
            WHERE school_id = (SELECT id FROM schools WHERE name = 'School A')
            LIMIT 1
            """, connection);
        var crossedError = await Assert.ThrowsAsync<PostgresException>(() => crossed.ExecuteNonQueryAsync());
        Assert.Equal("23503", crossedError.SqlState);

        await using var conflict = new NpgsqlCommand("""
            INSERT INTO timetable_entries
                (id, school_id, class_section_id, subject_id, day_of_week, starts_at, ends_at, room, created_at_utc)
            SELECT gen_random_uuid(), school_id, class_section_id, subject_id, day_of_week, starts_at, ends_at, 'A2', now()
            FROM timetable_entries
            WHERE room = 'A1'
            """, connection);
        var conflictError = await Assert.ThrowsAsync<PostgresException>(() => conflict.ExecuteNonQueryAsync());
        Assert.Equal("23505", conflictError.SqlState);
    }

    [Fact]
    public async Task Supervisor_home_is_limited_to_the_authorized_supervisor_context()
    {
        using var missing = await _client.GetAsync("/api/v1/supervisor/home");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var access = await AccessTokenAsync("supervisor.a", "SupervisorPass-123", "supervisor");
        using var otherSchool = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolBId,
            role = "supervisor"
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, otherSchool.StatusCode);

        using var withStudent = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "supervisor",
            studentId = _postgres.Seed.StudentAId
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, withStudent.StatusCode);

        using var selected = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "supervisor"
        }, access));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var token = (await ReadAsync(selected)).GetProperty("accessToken").GetString()!;

        using var home = await GetAsync("/api/v1/supervisor/home", token);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var body = await ReadAsync(home);
        Assert.Equal("Supervisor A", body.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rating").ValueKind);
        Assert.Equal(0, body.GetProperty("tripTotal").GetInt32());
        Assert.Equal(0, body.GetProperty("completedTripCount").GetInt32());
        Assert.Empty(body.GetProperty("trips").EnumerateArray());
        Assert.DoesNotContain("Student A", body.ToString());
        Assert.DoesNotContain("School B", body.ToString());

        using var studentProfile = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/profile", token);
        Assert.Equal(HttpStatusCode.Forbidden, studentProfile.StatusCode);
        Assert.DoesNotContain("Student A", await studentProfile.Content.ReadAsStringAsync());

        using var teacherHome = await GetAsync("/api/v1/teacher/home", token);
        Assert.Equal(HttpStatusCode.Forbidden, teacherHome.StatusCode);
        using var teacherSchedule = await GetAsync("/api/v1/teacher/schedule", token);
        Assert.Equal(HttpStatusCode.Forbidden, teacherSchedule.StatusCode);

        var teacherToken = await AccessTokenAsync("teacher.a", "TeacherPass-123", "teacher");
        using var teacherContext = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "teacher"
        }, teacherToken));
        Assert.Equal(HttpStatusCode.OK, teacherContext.StatusCode);
        var teacherContextToken = (await ReadAsync(teacherContext)).GetProperty("accessToken").GetString()!;
        using var deniedToTeacher = await GetAsync("/api/v1/supervisor/home", teacherContextToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedToTeacher.StatusCode);
        Assert.DoesNotContain("Supervisor A", await deniedToTeacher.Content.ReadAsStringAsync());

        var studentToken = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var deniedToStudent = await GetAsync("/api/v1/supervisor/home", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedToStudent.StatusCode);
        Assert.DoesNotContain("Supervisor A", await deniedToStudent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Student_cannot_select_another_school_or_another_student()
    {
        var access = await AccessTokenAsync("student.a", "StudentPass-123", "student");
        using var otherSchool = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolBId,
            role = "student",
            studentId = _postgres.Seed.StudentAId
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, otherSchool.StatusCode);

        var token = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var otherStudent = await GetAsync($"/api/v1/students/{_postgres.Seed.UnlinkedStudentId}/timetable", token);
        Assert.Equal(HttpStatusCode.NotFound, otherStudent.StatusCode);
    }

    [Fact]
    public async Task Invalid_credentials_unknown_user_and_missing_role_are_consistent()
    {
        using var wrong = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "student.a",
            password = "wrong-password",
            role = "student"
        });
        using var unknown = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "missing.user",
            password = "wrong-password",
            role = "student"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        using var missingRole = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "student.a",
            password = "StudentPass-123",
            role = "teacher"
        });
        Assert.Equal(HttpStatusCode.Forbidden, missingRole.StatusCode);
        Assert.Equal(ProblemResponses.Forbidden, (await ReadAsync(missingRole)).GetProperty("type").GetString());

        using var omittedStudent = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "student.a",
            password = "StudentPass-123"
        });
        Assert.Equal(HttpStatusCode.OK, omittedStudent.StatusCode);
        Assert.Contains((await ReadAsync(omittedStudent)).GetProperty("students").EnumerateArray(), student =>
            student.GetProperty("role").GetString() == "student");

        using var omittedParent = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "parent.a",
            password = "ParentPass-123"
        });
        Assert.Equal(HttpStatusCode.OK, omittedParent.StatusCode);
        Assert.Contains((await ReadAsync(omittedParent)).GetProperty("students").EnumerateArray(), student =>
            student.GetProperty("role").GetString() == "parent");

        using var omittedTeacher = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "teacher.a",
            password = "TeacherPass-123"
        });
        Assert.Equal(HttpStatusCode.OK, omittedTeacher.StatusCode);
        Assert.Contains((await ReadAsync(omittedTeacher)).GetProperty("memberships").EnumerateArray(), membership =>
            membership.GetProperty("role").GetString() == "teacher");

        using var omittedSupervisor = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "supervisor.a",
            password = "SupervisorPass-123"
        });
        Assert.Equal(HttpStatusCode.OK, omittedSupervisor.StatusCode);
        Assert.Contains((await ReadAsync(omittedSupervisor)).GetProperty("memberships").EnumerateArray(), membership =>
            membership.GetProperty("role").GetString() == "supervisor");

        using var invalid = await _client.PostAsJsonAsync("/api/v1/auth/login", new { username = "", password = "", role = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("urn:smartschool:validation", await invalid.Content.ReadAsStringAsync());

        using var anonymous = await _client.GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(ProblemResponses.Unauthorized, (await ReadAsync(anonymous)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Revoked_membership_blocks_the_existing_token_and_refresh()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "student.b",
            password = "StudentPass-123",
            role = "student"
        });
        var body = await ReadAsync(login);
        var access = body.GetProperty("accessToken").GetString()!;
        var refresh = body.GetProperty("refreshToken").GetString()!;
        using var selected = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "student",
            studentId = _postgres.Seed.UnlinkedStudentId
        }, access));
        var selectedBody = await ReadAsync(selected);
        var contextAccess = selectedBody.GetProperty("accessToken").GetString()!;
        var contextRefresh = selectedBody.GetProperty("refreshToken").GetString()!;

        await using var connection = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await connection.OpenAsync();
        await using var revoke = new NpgsqlCommand(
            "UPDATE user_memberships SET status = 'revoked' WHERE user_id = (SELECT id FROM users WHERE username = 'student.b')",
            connection);
        Assert.Equal(1, await revoke.ExecuteNonQueryAsync());

        using var profile = await GetAsync($"/api/v1/students/{_postgres.Seed.UnlinkedStudentId}/profile", contextAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);

        using var refreshed = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = contextRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        Assert.DoesNotContain(refresh, await refreshed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Temporary_password_blocks_student_data_until_changed()
    {
        var access = await AccessTokenAsync("temp.student", "Temporary-12345", "student");
        using var blocked = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "student",
            studentId = _postgres.Seed.TemporaryStudentId
        }, access));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(ProblemResponses.PasswordChange, (await ReadAsync(blocked)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Future_modules_are_not_exposed_and_database_isolation_holds()
    {
        using var fees = await _client.GetAsync("/api/v1/fees");
        using var homework = await _client.GetAsync("/api/v1/homework");
        using var registration = await _client.GetAsync("/api/v1/modules/homework/registration");
        Assert.Equal(HttpStatusCode.NotFound, fees.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, homework.StatusCode);
        Assert.Contains(ProblemResponses.NotFound, await fees.Content.ReadAsStringAsync());
        Assert.Contains(ProblemResponses.NotFound, await homework.Content.ReadAsStringAsync());

        var homeworkReferenced = typeof(Program).Assembly.GetReferencedAssemblies()
            .Any(static reference => reference.Name == "SmartSchool.Modules.Homework");
        if (homeworkReferenced)
        {
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
            var probe = await ReadAsync(registration);
            Assert.Equal("homework", probe.GetProperty("module").GetString());
            Assert.Equal("registered", probe.GetProperty("state").GetString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
            Assert.Contains(ProblemResponses.NotFound, await registration.Content.ReadAsStringAsync());
        }

        using var health = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("Healthy", await health.Content.ReadAsStringAsync());

        await using var app = new NpgsqlConnection(_postgres.AppConnectionString);
        await app.OpenAsync();
        await using (var unset = new NpgsqlCommand("SELECT count(*) FROM students", app))
        {
            Assert.Equal(0L, (long)(await unset.ExecuteScalarAsync() ?? 0L));
        }

        await using (var setSchool = new NpgsqlCommand("SELECT set_config('app.school_id', @school, false)", app))
        {
            setSchool.Parameters.AddWithValue("school", _postgres.Seed.SchoolAId.ToString("D"));
            await setSchool.ExecuteNonQueryAsync();
        }

        await using (var schoolA = new NpgsqlCommand("SELECT count(*) FROM students WHERE school_id = @school", app))
        {
            schoolA.Parameters.AddWithValue("school", _postgres.Seed.SchoolAId);
            Assert.Equal(3L, (long)(await schoolA.ExecuteScalarAsync() ?? 0L));
        }

        await using (var schoolB = new NpgsqlCommand("SELECT count(*) FROM students WHERE school_id = @school", app))
        {
            schoolB.Parameters.AddWithValue("school", _postgres.Seed.SchoolBId);
            Assert.Equal(0L, (long)(await schoolB.ExecuteScalarAsync() ?? 0L));
        }

        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using var history = new NpgsqlCommand(
            "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%InitialFoundation'",
            owner);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task App_role_startup_rejects_an_elevated_or_unknown_role()
    {
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var elevated = new NpgsqlCommand("ALTER ROLE smart_school_app CREATEROLE", owner))
            await elevated.ExecuteNonQueryAsync();
        try
        {
            await using var elevatedFactory = new ApiFactory(_postgres.AppConnectionString);
            var elevatedError = Assert.Throws<InvalidOperationException>(() => elevatedFactory.CreateClient());
            Assert.Contains("CREATEROLE", elevatedError.ToString());
        }
        finally
        {
            await using var restore = new NpgsqlCommand("ALTER ROLE smart_school_app NOCREATEROLE", owner);
            await restore.ExecuteNonQueryAsync();
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }

        var wrong = new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString)
        {
            Username = "smart_school_wrong",
            Password = "Wrong-role-password"
        };
        await using (var create = new NpgsqlCommand(
            "CREATE ROLE smart_school_wrong NOLOGIN NOINHERIT NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION",
            owner))
            await create.ExecuteNonQueryAsync();
        await using (var login = new NpgsqlCommand("ALTER ROLE smart_school_wrong LOGIN PASSWORD 'Wrong-role-password'", owner))
            await login.ExecuteNonQueryAsync();
        await using (var grant = new NpgsqlCommand("GRANT CONNECT ON DATABASE smart_school TO smart_school_wrong", owner))
            await grant.ExecuteNonQueryAsync();
        try
        {
            await using var wrongFactory = new ApiFactory(wrong.ConnectionString);
            var wrongError = Assert.Throws<InvalidOperationException>(() => wrongFactory.CreateClient());
            Assert.Contains(DatabaseSecurityCheck.RuntimeRole, wrongError.ToString());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var revoke = new NpgsqlCommand("REVOKE ALL PRIVILEGES ON DATABASE smart_school FROM smart_school_wrong", owner);
            await revoke.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand("DROP ROLE IF EXISTS smart_school_wrong", owner);
            await drop.ExecuteNonQueryAsync();
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
        }
    }

    [Fact]
    public async Task App_role_startup_rejects_a_missing_rls_policy()
    {
        await using var owner = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var drop = new NpgsqlCommand("DROP POLICY branches_tenant ON branches", owner))
            await drop.ExecuteNonQueryAsync();
        try
        {
            await using var factory = new ApiFactory(_postgres.AppConnectionString);
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("branches_tenant", error.ToString());
        }
        finally
        {
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
    public async Task App_role_startup_rejects_a_superuser_connection()
    {
        await using var factory = new ApiFactory(_postgres.OwnerConnectionString);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("BYPASSRLS", error.ToString());
        Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
    }

    [Fact]
    public async Task Bootstrap_admin_is_one_time_and_uses_the_app_role()
    {
        var previousDatabase = Environment.GetEnvironmentVariable("DATABASE_URL");
        var previousSchool = Environment.GetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME");
        var previousUser = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME");
        var previousPassword = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.AppConnectionString);
            Environment.SetEnvironmentVariable(DatabaseSecurityCheck.ExpectedDatabaseNameVariable, "smart_school");
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", "Bootstrap School");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", "bootstrap.admin");
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", "Bootstrap-password-123");

            Assert.Equal(0, await ProvisionAdminCommand.RunAsync());
            Assert.Equal(1, await ProvisionAdminCommand.RunAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", previousDatabase);
            Environment.SetEnvironmentVariable(
                DatabaseSecurityCheck.ExpectedDatabaseNameVariable,
                new NpgsqlConnectionStringBuilder(_postgres.AppConnectionString).Database);
            Environment.SetEnvironmentVariable("BOOTSTRAP_SCHOOL_NAME", previousSchool);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME", previousUser);
            Environment.SetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD", previousPassword);
        }
    }

    [Fact]
    public async Task Teacher_attendance_is_limited_to_assigned_classes_and_enrolled_students()
    {
        using var missing = await _client.GetAsync("/api/v1/teacher/attendance");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var teacherAccess = await AccessTokenAsync("teacher.a", "TeacherPass-123", "teacher");
        using var selected = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "teacher"
        }, teacherAccess));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var teacher = (await ReadAsync(selected)).GetProperty("accessToken").GetString()!;

        using var sheet = await GetAsync("/api/v1/teacher/attendance", teacher);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        var sheetBody = await ReadAsync(sheet);
        var schoolClass = sheetBody.GetProperty("classes").EnumerateArray().Single();
        Assert.Equal(_postgres.Seed.ClassAId, schoolClass.GetProperty("classSectionId").GetGuid());
        Assert.Equal("5-A", schoolClass.GetProperty("className").GetString());
        Assert.Equal(1, schoolClass.GetProperty("presentCount").GetInt32());
        Assert.Equal("Student A", schoolClass.GetProperty("students")[0].GetProperty("fullName").GetString());
        Assert.Equal("present", schoolClass.GetProperty("students")[0].GetProperty("status").GetString());
        Assert.DoesNotContain("1-B", sheetBody.ToString());
        Assert.DoesNotContain("Student C", sheetBody.ToString());

        var student = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var studentWrite = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassAId}/attendance", new
        {
            date = _postgres.Seed.Today.AddDays(3),
            marks = new[] { new { studentId = _postgres.Seed.StudentAId, status = "late" } }
        }, student));
        Assert.Equal(HttpStatusCode.Forbidden, studentWrite.StatusCode);

        var parent = await LoginAndSelectAsync("parent.a", "ParentPass-123", "parent", _postgres.Seed.StudentAId);
        using var parentSheet = await GetAsync("/api/v1/teacher/attendance", parent);
        Assert.Equal(HttpStatusCode.Forbidden, parentSheet.StatusCode);
        using var linked = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/attendance", parent);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using var unlinked = await GetAsync($"/api/v1/students/{_postgres.Seed.UnlinkedStudentId}/attendance", parent);
        Assert.Equal(HttpStatusCode.NotFound, unlinked.StatusCode);

        var supervisorAccess = await AccessTokenAsync("supervisor.a", "SupervisorPass-123", "supervisor");
        using var supervisorContext = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "supervisor"
        }, supervisorAccess));
        var supervisor = (await ReadAsync(supervisorContext)).GetProperty("accessToken").GetString()!;
        using var supervisorSheet = await GetAsync("/api/v1/teacher/attendance", supervisor);
        Assert.Equal(HttpStatusCode.Forbidden, supervisorSheet.StatusCode);

        using var otherClass = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassBId}/attendance", new
        {
            date = _postgres.Seed.Today.AddDays(3),
            marks = new[] { new { studentId = _postgres.Seed.StudentAId, status = "present" } }
        }, teacher));
        Assert.Equal(HttpStatusCode.Forbidden, otherClass.StatusCode);

        using var otherStudent = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassAId}/attendance", new
        {
            date = _postgres.Seed.Today.AddDays(3),
            marks = new[]
            {
                new { studentId = _postgres.Seed.StudentAId, status = "present" },
                new { studentId = _postgres.Seed.UnlinkedStudentId, status = "absent" }
            }
        }, teacher));
        Assert.Equal(HttpStatusCode.Forbidden, otherStudent.StatusCode);

        using var invalid = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassAId}/attendance", new
        {
            date = _postgres.Seed.Today.AddDays(3),
            marks = new[] { new { studentId = _postgres.Seed.StudentAId, status = "skipped" } }
        }, teacher));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var probe = _postgres.Seed.Today.AddDays(3);
        try
        {
            using var created = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassAId}/attendance", new
            {
                date = probe,
                marks = new[] { new { studentId = _postgres.Seed.StudentAId, status = "late" } }
            }, teacher));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal("late", (await ReadAsync(created)).GetProperty("classes")[0].GetProperty("students")[0].GetProperty("status").GetString());

            using var updated = await _client.SendAsync(Json(HttpMethod.Put, $"/api/v1/teacher/classes/{_postgres.Seed.ClassAId}/attendance", new
            {
                date = probe,
                marks = new[] { new { studentId = _postgres.Seed.StudentAId, status = "excused" } }
            }, teacher));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var updatedBody = await ReadAsync(updated);
            Assert.Equal(1, updatedBody.GetProperty("classes")[0].GetProperty("excusedCount").GetInt32());
            Assert.Equal("excused", updatedBody.GetProperty("classes")[0].GetProperty("students")[0].GetProperty("status").GetString());

            using var own = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/attendance", student);
            var ownBody = await ReadAsync(own);
            Assert.Contains("excused", ownBody.GetProperty("records").EnumerateArray().Select(row => row.GetProperty("status").GetString()));
            Assert.DoesNotContain(_postgres.Seed.OtherSchoolStudentId.ToString(), ownBody.ToString());
        }
        finally
        {
            await using var connection = new NpgsqlConnection(_postgres.OwnerConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand(
                "DELETE FROM attendance_records WHERE session_id IN (SELECT id FROM attendance_sessions WHERE session_date = @date); DELETE FROM attendance_sessions WHERE session_date = @date;",
                connection);
            cleanup.Parameters.AddWithValue("date", probe.ToDateTime(TimeOnly.MinValue));
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Attendance_constraints_reject_duplicates_and_cross_school_sessions()
    {
        await using var connection = new NpgsqlConnection(_postgres.OwnerConnectionString);
        await connection.OpenAsync();

        await using var duplicate = new NpgsqlCommand("""
            INSERT INTO attendance_records (id, school_id, session_id, student_id, status, created_at_utc)
            SELECT gen_random_uuid(), school_id, session_id, student_id, status, created_at_utc
            FROM attendance_records
            WHERE school_id = (SELECT id FROM schools WHERE name = 'School A')
            LIMIT 1
            """, connection);
        var duplicateError = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal("23505", duplicateError.SqlState);

        await using var crossSchool = new NpgsqlCommand("""
            INSERT INTO attendance_sessions (id, school_id, class_section_id, session_date, created_at_utc)
            VALUES (
              gen_random_uuid(),
              (SELECT id FROM schools WHERE name = 'School A'),
              (SELECT id FROM class_sections WHERE name = '1-B'),
              DATE '2026-01-15',
              NOW())
            """, connection);
        var crossSchoolError = await Assert.ThrowsAsync<PostgresException>(() => crossSchool.ExecuteNonQueryAsync());
        Assert.Equal("23503", crossSchoolError.SqlState);
    }

    [Fact]
    public async Task Homework_is_limited_to_the_assigned_class_and_the_linked_student()
    {
        var teacherAccess = await AccessTokenAsync("teacher.a", "TeacherPass-123", "teacher");
        using var selected = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role = "teacher"
        }, teacherAccess));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var teacher = (await ReadAsync(selected)).GetProperty("accessToken").GetString()!;

        using var missingTitle = await _client.SendAsync(Json(HttpMethod.Post, "/api/v1/teacher/homework", new
        {
            classSectionId = _postgres.Seed.ClassAId,
            title = " ",
            description = "",
            instructions = "",
            dueOn = _postgres.Seed.Today.AddDays(10)
        }, teacher));
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);

        using var otherClass = await _client.SendAsync(Json(HttpMethod.Post, "/api/v1/teacher/homework", new
        {
            classSectionId = _postgres.Seed.ClassBId,
            title = "Science homework",
            description = "Read the chapter.",
            instructions = "",
            dueOn = _postgres.Seed.Today.AddDays(10)
        }, teacher));
        Assert.Equal(HttpStatusCode.Forbidden, otherClass.StatusCode);

        using var created = await _client.SendAsync(Json(HttpMethod.Post, "/api/v1/teacher/homework", new
        {
            classSectionId = _postgres.Seed.ClassAId,
            title = "Algebra practice",
            description = "Solve the first ten questions.",
            instructions = "Show the working.",
            dueOn = _postgres.Seed.Today.AddDays(10)
        }, teacher));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await ReadAsync(created);
        var assignmentId = createdBody.GetProperty("id").GetGuid();
        Assert.Equal("Algebra practice", createdBody.GetProperty("title").GetString());
        Assert.Equal("Mathematics", createdBody.GetProperty("subjectName").GetString());
        Assert.Equal("5-A", createdBody.GetProperty("className").GetString());
        Assert.Equal("awaiting", createdBody.GetProperty("status").GetString());

        using var closed = await _client.SendAsync(Json(HttpMethod.Post, "/api/v1/teacher/homework", new
        {
            classSectionId = _postgres.Seed.ClassAId,
            title = "Past paper",
            description = "Already due.",
            instructions = "",
            dueOn = _postgres.Seed.Today.AddDays(-1)
        }, teacher));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedId = (await ReadAsync(closed)).GetProperty("id").GetGuid();

        var student = await LoginAndSelectAsync("student.a", "StudentPass-123", "student", _postgres.Seed.StudentAId);
        using var studentDenied = await GetAsync("/api/v1/teacher/homework", student);
        Assert.Equal(HttpStatusCode.Forbidden, studentDenied.StatusCode);

        using var list = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/homework", student);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await ReadAsync(list)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == assignmentId && item.GetProperty("status").GetString() == "awaiting");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == closedId && item.GetProperty("status").GetString() == "closed");

        using var teacherOnStudent = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/homework", teacher);
        Assert.Equal(HttpStatusCode.Forbidden, teacherOnStudent.StatusCode);

        using var closedSubmit = await _client.SendAsync(Json(
            HttpMethod.Post,
            $"/api/v1/students/{_postgres.Seed.StudentAId}/homework/{closedId}/submission",
            new { note = "late" },
            student));
        Assert.Equal(HttpStatusCode.Conflict, closedSubmit.StatusCode);

        using var submitted = await _client.SendAsync(Json(
            HttpMethod.Post,
            $"/api/v1/students/{_postgres.Seed.StudentAId}/homework/{assignmentId}/submission",
            new { note = "finished" },
            student));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("submitted", (await ReadAsync(submitted)).GetProperty("status").GetString());

        var parent = await LoginAndSelectAsync("parent.a", "ParentPass-123", "parent", _postgres.Seed.StudentAId);
        using var parentList = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/homework", parent);
        Assert.Equal(HttpStatusCode.OK, parentList.StatusCode);
        Assert.Contains((await ReadAsync(parentList)).GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == assignmentId && item.GetProperty("status").GetString() == "submitted");

        using var unlinked = await GetAsync($"/api/v1/students/{_postgres.Seed.UnlinkedStudentId}/homework", parent);
        Assert.Equal(HttpStatusCode.NotFound, unlinked.StatusCode);

        using var submissions = await GetAsync($"/api/v1/teacher/homework/{assignmentId}/submissions", teacher);
        Assert.Equal(HttpStatusCode.OK, submissions.StatusCode);
        var submission = (await ReadAsync(submissions)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Student A", submission.GetProperty("studentName").GetString());
        var submissionId = submission.GetProperty("id").GetGuid();

        using var reviewed = await _client.SendAsync(Json(
            HttpMethod.Put,
            $"/api/v1/teacher/homework/{assignmentId}/submissions/{submissionId}",
            new { note = "Good working." },
            teacher));
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal("reviewed", (await ReadAsync(reviewed)).GetProperty("status").GetString());

        using var afterReview = await GetAsync($"/api/v1/students/{_postgres.Seed.StudentAId}/homework", student);
        Assert.Contains((await ReadAsync(afterReview)).GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == assignmentId
            && item.GetProperty("status").GetString() == "reviewed"
            && item.GetProperty("note").GetString() == "Good working.");

        using var replaceReviewed = await _client.SendAsync(Json(
            HttpMethod.Post,
            $"/api/v1/students/{_postgres.Seed.StudentAId}/homework/{assignmentId}/submission",
            new { note = "again" },
            student));
        Assert.Equal(HttpStatusCode.Conflict, replaceReviewed.StatusCode);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/teacher/homework/{assignmentId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teacher);
        using var deleteReviewed = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.Conflict, deleteReviewed.StatusCode);
    }

    private async Task<string> LoginAndSelectAsync(string username, string password, string role, Guid studentId)
    {
        var access = await AccessTokenAsync(username, password, role);
        using var response = await _client.SendAsync(Json(HttpMethod.Put, "/api/v1/me/context", new
        {
            schoolId = _postgres.Seed.SchoolAId,
            role,
            studentId
        }, access));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<string> AccessTokenAsync(string username, string password, string role)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { username, password, role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> GetAsync(string url, string accessToken, Guid? schoolHeader = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (schoolHeader is Guid schoolId)
            request.Headers.Add("X-School-Id", schoolId.ToString("D"));
        request.Headers.Add("X-Role", "school_admin");
        return _client.SendAsync(request);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object body, string accessToken)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
