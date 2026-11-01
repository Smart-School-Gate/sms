using Microsoft.EntityFrameworkCore;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Teachers;

public sealed class TeacherService(AppDbContext db, RequestActor actor, TimeProvider time)
{
    public Task<ServiceResult<TeacherHomeResponse>> HomeAsync(CancellationToken cancellationToken) =>
        LoadAsync<TeacherHomeResponse>(cancellationToken, async (name, lessons) =>
        {
            var today = time.GetUtcNow().UtcDateTime.DayOfWeek.ToString().ToLowerInvariant();
            var todayCount = lessons.Count(lesson => lesson.Day == today);
            return new ServiceResult<TeacherHomeResponse>.Success(
                new TeacherHomeResponse(name, todayCount, todayCount, lessons));
        });

    public Task<ServiceResult<TeacherScheduleResponse>> ScheduleAsync(CancellationToken cancellationToken) =>
        LoadAsync<TeacherScheduleResponse>(cancellationToken, (_, lessons) =>
            Task.FromResult<ServiceResult<TeacherScheduleResponse>>(
                new ServiceResult<TeacherScheduleResponse>.Success(new TeacherScheduleResponse(lessons))));

    private async Task<ServiceResult<T>> LoadAsync<T>(
        CancellationToken cancellationToken,
        Func<string, IReadOnlyList<TeacherLessonResponse>, Task<ServiceResult<T>>> succeed)
    {
        if (!actor.IsAuthenticated)
        {
            return new ServiceResult<T>.Fail(
                StatusCodes.Status401Unauthorized,
                ProblemResponses.Unauthorized,
                "Unauthorized",
                "Authentication is required.");
        }

        if (actor.MustChangePassword)
        {
            return new ServiceResult<T>.Fail(
                StatusCodes.Status403Forbidden,
                ProblemResponses.PasswordChange,
                "Password change required",
                "Change the temporary password before continuing.");
        }

        if (actor.Context is null || actor.Context.Role != AppRole.Teacher)
        {
            return new ServiceResult<T>.Fail(
                StatusCodes.Status403Forbidden,
                ProblemResponses.Forbidden,
                "Forbidden",
                "A teacher context is required.");
        }

        return await succeed(actor.FullName, await LessonsAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<TeacherLessonResponse>> LessonsAsync(CancellationToken cancellationToken)
    {
        var schoolId = actor.Context!.SchoolId;
        var rows = await (
            from assignment in db.TeacherClassAssignments.AsNoTracking()
            join entry in db.TimetableEntries.AsNoTracking()
                on new { assignment.ClassSectionId, assignment.SubjectId, assignment.SchoolId }
                equals new { entry.ClassSectionId, entry.SubjectId, entry.SchoolId }
            join section in db.ClassSections.AsNoTracking() on entry.ClassSectionId equals section.Id
            join subject in db.Subjects.AsNoTracking() on entry.SubjectId equals subject.Id
            where assignment.UserId == actor.UserId && assignment.SchoolId == schoolId
            orderby entry.DayOfWeek, entry.StartsAt
            select new
            {
                entry.DayOfWeek,
                SubjectName = subject.Name,
                ClassName = section.Name,
                entry.StartsAt,
                entry.EndsAt
            }
        ).ToListAsync(cancellationToken);

        return rows.Select(row => new TeacherLessonResponse(
            row.DayOfWeek.ToString().ToLowerInvariant(),
            row.SubjectName,
            row.ClassName,
            row.StartsAt.ToString("HH:mm"),
            row.EndsAt.ToString("HH:mm"))).ToArray();
    }

    public async Task<ServiceResult<TeacherAttendanceResponse>> AttendanceAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        var denied = DenyTeacher<TeacherAttendanceResponse>();
        if (denied is not null)
            return denied;

        var schoolId = actor.Context!.SchoolId;
        return new ServiceResult<TeacherAttendanceResponse>.Success(
            await SheetAsync(schoolId, date, null, cancellationToken));
    }

    public async Task<ServiceResult<TeacherAttendanceResponse>> SaveAttendanceAsync(
        Guid classSectionId,
        TeacherAttendanceSaveRequest request,
        CancellationToken cancellationToken)
    {
        var denied = DenyTeacher<TeacherAttendanceResponse>();
        if (denied is not null)
            return denied;

        if (request.Date == default || request.Marks is null || request.Marks.Count == 0)
        {
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status400BadRequest,
                "urn:smartschool:validation",
                "The attendance request is not valid.",
                "A date and at least one student mark are required.");
        }

        if (request.Marks.Select(mark => mark.StudentId).Distinct().Count() != request.Marks.Count)
        {
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status400BadRequest,
                "urn:smartschool:validation",
                "The attendance request is not valid.",
                "A student can be marked only once for the same session.");
        }

        var parsed = new List<(Guid StudentId, AttendanceMark Status)>(request.Marks.Count);
        foreach (var mark in request.Marks)
        {
            if (!TryParseMark(mark.Status, out var status))
            {
                return Fail<TeacherAttendanceResponse>(
                    StatusCodes.Status400BadRequest,
                    "urn:smartschool:validation",
                    "The attendance status is not valid.",
                    "Status must be present, absent, late, or excused.");
            }

            parsed.Add((mark.StudentId, status));
        }

        var schoolId = actor.Context!.SchoolId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var assigned = await db.TeacherClassAssignments.AnyAsync(
            row => row.UserId == actor.UserId && row.SchoolId == schoolId && row.ClassSectionId == classSectionId,
            cancellationToken);
        if (!assigned)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status403Forbidden,
                ProblemResponses.Forbidden,
                "Forbidden",
                "The class is not assigned to this teacher.");
        }

        var section = await db.ClassSections.AsNoTracking()
            .Where(row => row.Id == classSectionId && row.SchoolId == schoolId)
            .Select(row => new { row.AcademicYearId })
            .FirstOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status403Forbidden,
                ProblemResponses.Forbidden,
                "Forbidden",
                "The class is not assigned to this teacher.");
        }

        var enrolled = await db.Enrollments.AsNoTracking()
            .Where(row => row.SchoolId == schoolId && row.ClassSectionId == classSectionId && row.AcademicYearId == section.AcademicYearId)
            .Select(row => row.StudentId)
            .ToListAsync(cancellationToken);
        if (parsed.Any(mark => !enrolled.Contains(mark.StudentId)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status403Forbidden,
                ProblemResponses.Forbidden,
                "Forbidden",
                "The student is not enrolled in this class.");
        }

        var session = await db.AttendanceSessions
            .FirstOrDefaultAsync(row => row.SchoolId == schoolId && row.ClassSectionId == classSectionId && row.SessionDate == request.Date, cancellationToken);
        if (session is null)
        {
            session = new AttendanceSession
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolId,
                ClassSectionId = classSectionId,
                SessionDate = request.Date,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime
            };
            db.AttendanceSessions.Add(session);
        }

        var existing = await db.AttendanceRecords
            .Where(row => row.SessionId == session.Id)
            .ToListAsync(cancellationToken);
        foreach (var mark in parsed)
        {
            var record = existing.FirstOrDefault(row => row.StudentId == mark.StudentId);
            if (record is null)
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    Id = Guid.NewGuid(),
                    SchoolId = schoolId,
                    SessionId = session.Id,
                    StudentId = mark.StudentId,
                    Status = mark.Status,
                    CreatedAtUtc = time.GetUtcNow().UtcDateTime
                });
            }
            else
            {
                record.Status = mark.Status;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail<TeacherAttendanceResponse>(
                StatusCodes.Status409Conflict,
                "urn:smartschool:conflict",
                "Attendance could not be saved.",
                "The attendance session conflicts with an existing record.");
        }

        return new ServiceResult<TeacherAttendanceResponse>.Success(
            await SheetAsync(schoolId, request.Date, classSectionId, cancellationToken));
    }

    private async Task<TeacherAttendanceResponse> SheetAsync(
        Guid schoolId,
        DateOnly? sessionDate,
        Guid? classSectionId,
        CancellationToken cancellationToken)
    {
        var roster = await (
            from assignment in db.TeacherClassAssignments.AsNoTracking()
            join section in db.ClassSections.AsNoTracking() on assignment.ClassSectionId equals section.Id
            join school in db.Schools.AsNoTracking() on section.SchoolId equals school.Id
            where assignment.UserId == actor.UserId
                && assignment.SchoolId == schoolId
                && (classSectionId == null || assignment.ClassSectionId == classSectionId)
            select new
            {
                section.Id,
                section.Name,
                section.AcademicYearId,
                school.TimeZoneId,
                Students = (
                    from enrollment in db.Enrollments.AsNoTracking()
                    join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.ClassSectionId == section.Id && enrollment.AcademicYearId == section.AcademicYearId
                    orderby student.FullName
                    select new { student.Id, student.FullName }
                ).ToList()
            }
        ).ToListAsync(cancellationToken);
        var sections = roster
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .OrderBy(row => row.Name)
            .ToArray();
        var timeZoneId = sections.Length == 0
            ? await db.Schools.AsNoTracking().Where(school => school.Id == schoolId).Select(school => school.TimeZoneId).FirstAsync(cancellationToken)
            : sections[0].TimeZoneId;
        var date = sessionDate ?? Today(timeZoneId);
        var classIds = sections.Select(row => row.Id).ToArray();
        var marks = classIds.Length == 0
            ? []
            : await (
                from record in db.AttendanceRecords.AsNoTracking()
                join session in db.AttendanceSessions.AsNoTracking() on record.SessionId equals session.Id
                where classIds.Contains(session.ClassSectionId) && session.SessionDate == date
                select new { session.ClassSectionId, record.StudentId, record.Status }
            ).ToListAsync(cancellationToken);
        var students = sections.SelectMany(section => section.Students.Select(student => new
        {
            ClassSectionId = section.Id,
            section.AcademicYearId,
            studentId = student.Id,
            student.FullName
        })).ToArray();

        var classes = sections
            .OrderBy(row => row.Name)
            .Select(section =>
            {
                var rows = students
                    .Where(student => student.ClassSectionId == section.Id && student.AcademicYearId == section.AcademicYearId)
                    .Select(student =>
                    {
                        var mark = marks.FirstOrDefault(item => item.ClassSectionId == section.Id && item.StudentId == student.studentId);
                        return new TeacherAttendanceStudentResponse(
                            student.studentId,
                            student.FullName,
                            mark is null ? null : mark.Status.ToString().ToLowerInvariant());
                    })
                    .ToArray();
                return new TeacherAttendanceClassResponse(
                    section.Id,
                    section.Name,
                    rows.Count(row => row.Status == "present"),
                    rows.Count(row => row.Status == "late"),
                    rows.Count(row => row.Status == "absent"),
                    rows.Count(row => row.Status == "excused"),
                    rows.Count(row => row.Status is null),
                    rows);
            })
            .ToArray();
        return new TeacherAttendanceResponse(date, classes);
    }

    private ServiceResult<T>? DenyTeacher<T>()
    {
        if (!actor.IsAuthenticated)
        {
            return Fail<T>(StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "Authentication is required.");
        }

        if (actor.MustChangePassword)
        {
            return Fail<T>(StatusCodes.Status403Forbidden, ProblemResponses.PasswordChange, "Password change required", "Change the temporary password before continuing.");
        }

        if (actor.Context is null || actor.Context.Role != AppRole.Teacher)
        {
            return Fail<T>(StatusCodes.Status403Forbidden, ProblemResponses.Forbidden, "Forbidden", "A teacher context is required.");
        }

        return null;
    }

    private DateOnly Today(string timeZoneId)
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

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, zone));
    }

    private static bool TryParseMark(string? value, out AttendanceMark mark)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "present":
                mark = AttendanceMark.Present;
                return true;
            case "absent":
                mark = AttendanceMark.Absent;
                return true;
            case "late":
                mark = AttendanceMark.Late;
                return true;
            case "excused":
                mark = AttendanceMark.Excused;
                return true;
            default:
                mark = default;
                return false;
        }
    }

    private static ServiceResult<T> Fail<T>(int status, string type, string title, string detail) =>
        new ServiceResult<T>.Fail(status, type, title, detail);
}
