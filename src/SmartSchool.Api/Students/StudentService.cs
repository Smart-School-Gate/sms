using Microsoft.EntityFrameworkCore;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Students;

public sealed class StudentService(AppDbContext db, RequestActor actor, TimeProvider time)
{
    public async Task<ServiceResult<StudentProfileResponse>> ProfileAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var denied = await DenyUnlessActiveStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return denied.As<StudentProfileResponse>();

        var placement = await LoadPlacementAsync(studentId, cancellationToken);
        if (placement is null)
            return NotFound<StudentProfileResponse>();

        return new ServiceResult<StudentProfileResponse>.Success(placement.Profile);
    }

    public async Task<ServiceResult<StudentHomeResponse>> HomeAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var denied = await DenyUnlessActiveStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return denied.As<StudentHomeResponse>();

        var home = await LoadHomeAsync(studentId, cancellationToken);
        if (home is null)
            return NotFound<StudentHomeResponse>();

        var today = Today(home.TimeZoneId);
        var slots = home.Slots
            .Where(slot => slot.Day == today.DayOfWeek)
            .Select(slot => new TimetableSlotResponse(slot.Subject, Format(slot.StartsAt), Format(slot.EndsAt), slot.Room))
            .ToArray();
        var deadlines = await OpenHomeworkDeadlinesAsync(studentId, today, cancellationToken);
        return new ServiceResult<StudentHomeResponse>.Success(new StudentHomeResponse(
            home.Profile.StudentId,
            home.Profile.FullName,
            new AttendanceSummaryResponse(home.Attended, home.Total, Fraction(home.Attended, home.Total)),
            slots,
            deadlines,
            home.Profile.GradeLevel?.Name,
            actor.FullName));
    }

    public async Task<ServiceResult<StudentAttendanceResponse>> AttendanceAsync(
        Guid studentId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var denied = await DenyUnlessActiveStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return denied.As<StudentAttendanceResponse>();

        var placement = await LoadAttendanceAsync(studentId, cancellationToken);
        if (placement is null)
            return NotFound<StudentAttendanceResponse>();

        var start = from ?? placement.YearStart ?? Today(placement.TimeZoneId).AddDays(-30);
        var end = to ?? placement.YearEnd ?? Today(placement.TimeZoneId);
        if (end < start)
        {
            return new ServiceResult<StudentAttendanceResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "urn:smartschool:validation",
                "The date range is not valid.",
                "The end date must be on or after the start date.");
        }

        var records = placement.Records
            .Where(record => record.Date >= start && record.Date <= end)
            .OrderBy(record => record.Date)
            .ToArray();

        var present = records.Count(x => x.Status == AttendanceMark.Present);
        var late = records.Count(x => x.Status == AttendanceMark.Late);
        var absent = records.Count(x => x.Status == AttendanceMark.Absent);
        var excused = records.Count(x => x.Status == AttendanceMark.Excused);
        return new ServiceResult<StudentAttendanceResponse>.Success(new StudentAttendanceResponse(
            studentId,
            start,
            end,
            present,
            late,
            absent,
            excused,
            records.Length,
            Fraction(present + late, records.Length),
            records.Select(x => new AttendanceRecordResponse(x.Date, x.Status.ToString().ToLowerInvariant())).ToArray()));
    }

    public async Task<ServiceResult<StudentTimetableResponse>> TimetableAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var denied = await DenyUnlessActiveStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return denied.As<StudentTimetableResponse>();

        var placement = await LoadTimetableAsync(studentId, cancellationToken);
        if (placement is null)
            return NotFound<StudentTimetableResponse>();

        if (placement.ClassSectionId is not Guid classId)
        {
            return new ServiceResult<StudentTimetableResponse>.Success(new StudentTimetableResponse(studentId, null, []));
        }

        var entries = placement.Entries
            .OrderBy(row => row.Day)
            .ThenBy(row => row.StartsAt)
            .Select(row => new TimetableEntryResponse(
                row.Day.ToString().ToLowerInvariant(),
                row.Subject,
                Format(row.StartsAt),
                Format(row.EndsAt),
                row.Room))
            .ToArray();

        return new ServiceResult<StudentTimetableResponse>.Success(
            new StudentTimetableResponse(studentId, classId, entries));
    }

    private async Task<Denied?> DenyUnlessActiveStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
            return Denied.Unauthorized();
        if (actor.MustChangePassword)
            return Denied.PasswordChange();
        if (actor.Context is null || actor.Context.Role is not (AppRole.Student or AppRole.Parent))
            return Denied.Forbidden("A student or parent context is required.");
        if (actor.Context.StudentId != studentId)
        {
            await AuditDenialAsync(studentId, cancellationToken);
            return Denied.NotFound();
        }

        return null;
    }

    private async Task AuditDenialAsync(Guid studentId, CancellationToken cancellationToken)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor.UserId,
            SchoolId = actor.Context?.SchoolId,
            Role = actor.Context is null ? null : RoleNames.ToName(actor.Context.Role),
            Action = "student.access_denied",
            EntityName = "student",
            EntityId = studentId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<HomeRow?> LoadHomeAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var row = await (
            from student in db.Students.AsNoTracking()
            join school in db.Schools.AsNoTracking() on student.SchoolId equals school.Id
            join branch in db.Branches.AsNoTracking() on student.BranchId equals branch.Id
            where student.Id == studentId
            select new
            {
                student.Id,
                student.FullName,
                student.StudentCode,
                student.DateOfBirth,
                Email = db.Users.Where(user => student.UserId != null && user.Id == student.UserId).Select(user => user.Email).FirstOrDefault(),
                Phone = db.Users.Where(user => student.UserId != null && user.Id == student.UserId).Select(user => user.Phone).FirstOrDefault(),
                SchoolId = school.Id,
                SchoolName = school.Name,
                school.TimeZoneId,
                BranchId = branch.Id,
                BranchName = branch.Name,
                Enrollment = (
                    from item in db.Enrollments.AsNoTracking()
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    join section in db.ClassSections.AsNoTracking() on item.ClassSectionId equals section.Id
                    join grade in db.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    select new { item.ClassSectionId, YearId = year.Id, YearName = year.Name, year.StartsOn, year.EndsOn, SectionName = section.Name, GradeId = grade.Id, GradeName = grade.Name }
                ).FirstOrDefault(),
                Attended = db.AttendanceRecords.Count(record =>
                    record.StudentId == student.Id
                    && (record.Status == AttendanceMark.Present || record.Status == AttendanceMark.Late)
                    && db.AttendanceSessions.Any(session =>
                        session.Id == record.SessionId
                        && (!db.Enrollments.Any(item => item.StudentId == student.Id && db.AcademicYears.Any(year => year.Id == item.AcademicYearId && year.IsCurrent))
                            || db.Enrollments.Any(item => item.StudentId == student.Id && db.AcademicYears.Any(year => year.Id == item.AcademicYearId && year.IsCurrent && session.SessionDate >= year.StartsOn && session.SessionDate <= year.EndsOn))))),
                Total = db.AttendanceRecords.Count(record =>
                    record.StudentId == student.Id
                    && db.AttendanceSessions.Any(session =>
                        session.Id == record.SessionId
                        && (!db.Enrollments.Any(item => item.StudentId == student.Id && db.AcademicYears.Any(year => year.Id == item.AcademicYearId && year.IsCurrent))
                            || db.Enrollments.Any(item => item.StudentId == student.Id && db.AcademicYears.Any(year => year.Id == item.AcademicYearId && year.IsCurrent && session.SessionDate >= year.StartsOn && session.SessionDate <= year.EndsOn))))),
                Slots = (
                    from entry in db.TimetableEntries.AsNoTracking()
                    join subject in db.Subjects.AsNoTracking() on entry.SubjectId equals subject.Id
                    join item in db.Enrollments.AsNoTracking() on entry.ClassSectionId equals item.ClassSectionId
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    orderby entry.DayOfWeek, entry.StartsAt
                    select new LessonRow(entry.DayOfWeek, subject.Name, entry.StartsAt, entry.EndsAt, entry.Room)
                ).ToList()
            }
        ).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;
        var enrollment = row.Enrollment;
        var profile = new StudentProfileResponse(
            row.Id,
            row.FullName,
            row.StudentCode,
            row.DateOfBirth,
            row.Email,
            row.Phone,
            new NamedId(row.SchoolId, row.SchoolName),
            new NamedId(row.BranchId, row.BranchName),
            enrollment is null ? null : new NamedId(enrollment.GradeId, enrollment.GradeName),
            enrollment is null ? null : new NamedId(enrollment.ClassSectionId, enrollment.SectionName),
            enrollment is null ? null : new NamedId(enrollment.YearId, enrollment.YearName));
        return new HomeRow(profile, row.TimeZoneId, row.Attended, row.Total, row.Slots);
    }

    private async Task<AttendanceRow?> LoadAttendanceAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var row = await (
            from student in db.Students.AsNoTracking()
            join school in db.Schools.AsNoTracking() on student.SchoolId equals school.Id
            where student.Id == studentId
            select new
            {
                school.TimeZoneId,
                Year = (
                    from item in db.Enrollments.AsNoTracking()
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    select new { year.StartsOn, year.EndsOn }
                ).FirstOrDefault(),
                Records = (
                    from record in db.AttendanceRecords.AsNoTracking()
                    join session in db.AttendanceSessions.AsNoTracking() on record.SessionId equals session.Id
                    where record.StudentId == student.Id
                    orderby session.SessionDate
                    select new MarkRow(session.SessionDate, record.Status)
                ).ToList()
            }
        ).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new AttendanceRow(row.TimeZoneId, row.Year?.StartsOn, row.Year?.EndsOn, row.Records);
    }

    private async Task<TimetableRow?> LoadTimetableAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var row = await (
            from student in db.Students.AsNoTracking()
            join school in db.Schools.AsNoTracking() on student.SchoolId equals school.Id
            where student.Id == studentId
            select new
            {
                ClassSectionId = (
                    from item in db.Enrollments.AsNoTracking()
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    select (Guid?)item.ClassSectionId
                ).FirstOrDefault(),
                Entries = (
                    from entry in db.TimetableEntries.AsNoTracking()
                    join subject in db.Subjects.AsNoTracking() on entry.SubjectId equals subject.Id
                    join item in db.Enrollments.AsNoTracking() on entry.ClassSectionId equals item.ClassSectionId
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    select new LessonRow(entry.DayOfWeek, subject.Name, entry.StartsAt, entry.EndsAt, entry.Room)
                ).ToList()
            }
        ).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new TimetableRow(row.ClassSectionId, row.Entries);
    }

    private async Task<Placement?> LoadPlacementAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var row = await (
            from student in db.Students.AsNoTracking()
            join school in db.Schools.AsNoTracking() on student.SchoolId equals school.Id
            join branch in db.Branches.AsNoTracking() on student.BranchId equals branch.Id
            where student.Id == studentId
            select new
            {
                student.Id,
                student.FullName,
                student.StudentCode,
                student.DateOfBirth,
                Email = db.Users.Where(user => student.UserId != null && user.Id == student.UserId).Select(user => user.Email).FirstOrDefault(),
                Phone = db.Users.Where(user => student.UserId != null && user.Id == student.UserId).Select(user => user.Phone).FirstOrDefault(),
                SchoolId = school.Id,
                SchoolName = school.Name,
                school.TimeZoneId,
                BranchId = branch.Id,
                BranchName = branch.Name,
                Enrollment = (
                    from item in db.Enrollments.AsNoTracking()
                    join year in db.AcademicYears.AsNoTracking() on item.AcademicYearId equals year.Id
                    join section in db.ClassSections.AsNoTracking() on item.ClassSectionId equals section.Id
                    join grade in db.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
                    where item.StudentId == student.Id && year.IsCurrent
                    select new
                    {
                        item.ClassSectionId,
                        YearId = year.Id,
                        YearName = year.Name,
                        year.StartsOn,
                        year.EndsOn,
                        SectionName = section.Name,
                        GradeId = grade.Id,
                        GradeName = grade.Name
                    }
                ).FirstOrDefault()
            }
        ).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var enrollment = row.Enrollment;
        var profile = new StudentProfileResponse(
            row.Id,
            row.FullName,
            row.StudentCode,
            row.DateOfBirth,
            row.Email,
            row.Phone,
            new NamedId(row.SchoolId, row.SchoolName),
            new NamedId(row.BranchId, row.BranchName),
            enrollment is null ? null : new NamedId(enrollment.GradeId, enrollment.GradeName),
            enrollment is null ? null : new NamedId(enrollment.ClassSectionId, enrollment.SectionName),
            enrollment is null ? null : new NamedId(enrollment.YearId, enrollment.YearName));

        return new Placement(
            profile,
            row.TimeZoneId,
            enrollment?.ClassSectionId,
            enrollment?.StartsOn,
            enrollment?.EndsOn);
    }

    private async Task<DeadlineResponse[]> OpenHomeworkDeadlinesAsync(
        Guid studentId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from enrollment in db.Enrollments.AsNoTracking()
            join year in db.AcademicYears.AsNoTracking() on enrollment.AcademicYearId equals year.Id
            join assignment in db.HomeworkAssignments.AsNoTracking() on enrollment.ClassSectionId equals assignment.ClassSectionId
            where enrollment.StudentId == studentId && year.IsCurrent && assignment.DueOn >= today
            orderby assignment.DueOn, assignment.CreatedAtUtc
            select new { assignment.Id, assignment.Title, assignment.DueOn }
        ).Take(4).ToListAsync(cancellationToken);

        return rows.Select(row => new DeadlineResponse(
            "homework",
            row.Id,
            row.Title,
            row.DueOn.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc))).ToArray();
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

        var local = TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, zone);
        return DateOnly.FromDateTime(local);
    }

    private static decimal Fraction(int attended, int total) =>
        total == 0 ? 0 : decimal.Round((decimal)attended / total, 4, MidpointRounding.AwayFromZero);

    private static string Format(TimeOnly value) => value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record LessonRow(DayOfWeek Day, string Subject, TimeOnly StartsAt, TimeOnly EndsAt, string? Room);

    private sealed record MarkRow(DateOnly Date, AttendanceMark Status);

    private sealed record HomeRow(StudentProfileResponse Profile, string TimeZoneId, int Attended, int Total, IReadOnlyList<LessonRow> Slots);

    private sealed record AttendanceRow(string TimeZoneId, DateOnly? YearStart, DateOnly? YearEnd, IReadOnlyList<MarkRow> Records);

    private sealed record TimetableRow(Guid? ClassSectionId, IReadOnlyList<LessonRow> Entries);

    private sealed record Placement(
        StudentProfileResponse Profile,
        string TimeZoneId,
        Guid? ClassSectionId,
        DateOnly? YearStart,
        DateOnly? YearEnd);

    private sealed record Denied(ServiceResult<bool> Result)
    {
        public ServiceResult<T> As<T>() => Result switch
        {
            ServiceResult<bool>.Fail fail => new ServiceResult<T>.Fail(fail.Status, fail.Type, fail.Title, fail.Detail),
            _ => new ServiceResult<T>.Fail(500, "urn:smartschool:internal", "Internal Server Error", "The request could not be completed.")
        };

        public static Denied Unauthorized() => new(new ServiceResult<bool>.Fail(
            StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "Authentication is required."));

        public static Denied PasswordChange() => new(new ServiceResult<bool>.Fail(
            StatusCodes.Status403Forbidden, ProblemResponses.PasswordChange, "Password change required", "Change the temporary password before continuing."));

        public static Denied Forbidden(string detail) => new(new ServiceResult<bool>.Fail(
            StatusCodes.Status403Forbidden, ProblemResponses.Forbidden, "Forbidden", detail));

        public static Denied NotFound() => new(new ServiceResult<bool>.Fail(
            StatusCodes.Status404NotFound, ProblemResponses.NotFound, "Not Found", "The student was not found."));
    }

    private static ServiceResult<T> NotFound<T>() =>
        new ServiceResult<T>.Fail(StatusCodes.Status404NotFound, ProblemResponses.NotFound, "Not Found", "The student was not found.");
}
