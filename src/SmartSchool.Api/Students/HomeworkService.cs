using Microsoft.EntityFrameworkCore;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Students;

public sealed class HomeworkService(AppDbContext db, RequestActor actor, TimeProvider time)
{
    public async Task<ServiceResult<HomeworkListResponse>> ListForStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var denied = await DenyStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return denied;

        var schoolId = actor.Context!.SchoolId;
        var rows = await LoadStudentRowsAsync(studentId, schoolId, cancellationToken);
        if (rows.Count == 0)
            return new ServiceResult<HomeworkListResponse>.Success(new HomeworkListResponse([]));

        var today = Today(rows[0].TimeZoneId);
        return new ServiceResult<HomeworkListResponse>.Success(new HomeworkListResponse(
            rows.Select(row => ToItem(row, today)).ToArray()));
    }

    public async Task<ServiceResult<HomeworkItemResponse>> SubmitAsync(
        Guid studentId,
        Guid assignmentId,
        string? note,
        CancellationToken cancellationToken)
    {
        var denied = await DenyStudentAsync(studentId, cancellationToken);
        if (denied is not null)
            return new ServiceResult<HomeworkItemResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var trimmed = note?.Trim();
        if (trimmed is { Length: > 2000 })
        {
            return Fail<HomeworkItemResponse>(
                StatusCodes.Status400BadRequest,
                "The note is too long.",
                "A submission note can contain at most 2000 characters.");
        }

        var schoolId = actor.Context!.SchoolId;
        var row = (await LoadStudentRowsAsync(studentId, schoolId, cancellationToken))
            .FirstOrDefault(item => item.Id == assignmentId);
        if (row is null)
            return Fail<HomeworkItemResponse>(StatusCodes.Status404NotFound, "Not Found", "The homework was not found.");

        var today = Today(row.TimeZoneId);
        if (row.SubmissionStatus is "reviewed")
        {
            return Fail<HomeworkItemResponse>(
                StatusCodes.Status409Conflict,
                "The homework can no longer be changed.",
                "A reviewed submission cannot be replaced.");
        }

        if (row.SubmissionStatus is null && row.DueOn < today)
        {
            return Fail<HomeworkItemResponse>(
                StatusCodes.Status409Conflict,
                "The homework is closed.",
                "The due date has passed and there is no submission.");
        }

        var existing = await db.HomeworkSubmissions
            .FirstOrDefaultAsync(item => item.AssignmentId == assignmentId && item.StudentId == studentId, cancellationToken);
        if (existing is null)
        {
            db.HomeworkSubmissions.Add(new HomeworkSubmission
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolId,
                AssignmentId = assignmentId,
                StudentId = studentId,
                Status = "submitted",
                Note = string.IsNullOrEmpty(trimmed) ? null : trimmed,
                SubmittedAtUtc = time.GetUtcNow().UtcDateTime
            });
        }
        else
        {
            existing.Status = "submitted";
            existing.Note = string.IsNullOrEmpty(trimmed) ? existing.Note : trimmed;
            existing.SubmittedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
        var saved = row with { SubmissionStatus = "submitted", Note = string.IsNullOrEmpty(trimmed) ? row.Note : trimmed };
        return new ServiceResult<HomeworkItemResponse>.Success(ToItem(saved, today));
    }

    public async Task<ServiceResult<TeacherClassOptionListResponse>> ClassesAsync(CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<TeacherClassOptionListResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var schoolId = actor.Context!.SchoolId;
        var items = await (
            from link in db.TeacherClassAssignments.AsNoTracking()
            join section in db.ClassSections.AsNoTracking() on link.ClassSectionId equals section.Id
            join grade in db.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
            join subject in db.Subjects.AsNoTracking() on link.SubjectId equals subject.Id
            where link.UserId == actor.UserId && link.SchoolId == schoolId
            orderby grade.SortOrder, section.Name, subject.Name
            select new TeacherClassOptionResponse(section.Id, subject.Id, grade.Name, section.Name, subject.Name)
        ).ToListAsync(cancellationToken);
        return new ServiceResult<TeacherClassOptionListResponse>.Success(new TeacherClassOptionListResponse(items));
    }

    public async Task<ServiceResult<HomeworkListResponse>> ListForTeacherAsync(CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<HomeworkListResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var schoolId = actor.Context!.SchoolId;
        var rows = await LoadTeacherRowsAsync(schoolId, cancellationToken);
        var today = rows.Count == 0 ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime) : Today(rows[0].TimeZoneId);
        return new ServiceResult<HomeworkListResponse>.Success(new HomeworkListResponse(
            rows.Select(row => ToItem(row, today)).ToArray()));
    }

    public async Task<ServiceResult<HomeworkItemResponse>> CreateAsync(CreateHomeworkRequest request, CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<HomeworkItemResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var invalid = ValidateText<HomeworkItemResponse>(request.Title, request.Description, request.Instructions, request.DueOn);
        if (invalid is not null)
            return new ServiceResult<HomeworkItemResponse>.Fail(invalid.Status, invalid.Type, invalid.Title, invalid.Detail);

        var schoolId = actor.Context!.SchoolId;
        var subjectId = request.SubjectId;
        if (subjectId is null)
        {
            var subjects = await db.TeacherClassAssignments.AsNoTracking()
                .Where(link => link.UserId == actor.UserId && link.SchoolId == schoolId && link.ClassSectionId == request.ClassSectionId)
                .Select(link => link.SubjectId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (subjects.Count == 0)
            {
                return Fail<HomeworkItemResponse>(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "The class is not assigned to this teacher.");
            }

            if (subjects.Count != 1)
            {
                return Fail<HomeworkItemResponse>(
                    StatusCodes.Status400BadRequest,
                    "The subject is required.",
                    "Choose one subject assigned to this teacher for the class.");
            }

            subjectId = subjects[0];
        }

        var assigned = await db.TeacherClassAssignments.AsNoTracking().AnyAsync(
            link => link.UserId == actor.UserId
                && link.SchoolId == schoolId
                && link.ClassSectionId == request.ClassSectionId
                && link.SubjectId == subjectId,
            cancellationToken);
        if (!assigned)
        {
            return Fail<HomeworkItemResponse>(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The class is not assigned to this teacher.");
        }

        var assignment = new HomeworkAssignment
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            ClassSectionId = request.ClassSectionId,
            SubjectId = subjectId.Value,
            TeacherUserId = actor.UserId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Instructions = request.Instructions.Trim(),
            DueOn = request.DueOn,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        db.HomeworkAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        var created = (await LoadTeacherRowsAsync(schoolId, cancellationToken)).First(item => item.Id == assignment.Id);
        return new ServiceResult<HomeworkItemResponse>.Success(ToItem(created, Today(created.TimeZoneId)));
    }

    public async Task<ServiceResult<HomeworkItemResponse>> UpdateAsync(
        Guid assignmentId,
        UpdateHomeworkRequest request,
        CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<HomeworkItemResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var invalid = ValidateText<HomeworkItemResponse>(request.Title, request.Description, request.Instructions, request.DueOn);
        if (invalid is not null)
            return new ServiceResult<HomeworkItemResponse>.Fail(invalid.Status, invalid.Type, invalid.Title, invalid.Detail);

        var schoolId = actor.Context!.SchoolId;
        var assignment = await db.HomeworkAssignments.FirstOrDefaultAsync(
            item => item.Id == assignmentId && item.TeacherUserId == actor.UserId && item.SchoolId == schoolId,
            cancellationToken);
        if (assignment is null)
            return Fail<HomeworkItemResponse>(StatusCodes.Status404NotFound, "Not Found", "The homework was not found.");

        assignment.Title = request.Title.Trim();
        assignment.Description = request.Description.Trim();
        assignment.Instructions = request.Instructions.Trim();
        assignment.DueOn = request.DueOn;
        await db.SaveChangesAsync(cancellationToken);

        var updated = (await LoadTeacherRowsAsync(schoolId, cancellationToken)).First(item => item.Id == assignmentId);
        return new ServiceResult<HomeworkItemResponse>.Success(ToItem(updated, Today(updated.TimeZoneId)));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<bool>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var schoolId = actor.Context!.SchoolId;
        var assignment = await db.HomeworkAssignments.FirstOrDefaultAsync(
            item => item.Id == assignmentId && item.TeacherUserId == actor.UserId && item.SchoolId == schoolId,
            cancellationToken);
        if (assignment is null)
            return Fail<bool>(StatusCodes.Status404NotFound, "Not Found", "The homework was not found.");

        var hasSubmission = await db.HomeworkSubmissions.AnyAsync(item => item.AssignmentId == assignmentId, cancellationToken);
        if (hasSubmission)
        {
            return Fail<bool>(
                StatusCodes.Status409Conflict,
                "The homework has submissions.",
                "A homework that already has a student submission cannot be deleted.");
        }

        db.HomeworkAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<HomeworkSubmissionListResponse>> SubmissionsAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<HomeworkSubmissionListResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var schoolId = actor.Context!.SchoolId;
        var owned = await db.HomeworkAssignments.AsNoTracking().AnyAsync(
            item => item.Id == assignmentId && item.TeacherUserId == actor.UserId && item.SchoolId == schoolId,
            cancellationToken);
        if (!owned)
            return Fail<HomeworkSubmissionListResponse>(StatusCodes.Status404NotFound, "Not Found", "The homework was not found.");

        var items = await (
            from submission in db.HomeworkSubmissions.AsNoTracking()
            join student in db.Students.AsNoTracking() on submission.StudentId equals student.Id
            where submission.AssignmentId == assignmentId && submission.SchoolId == schoolId
            orderby submission.SubmittedAtUtc
            select new HomeworkSubmissionResponse(
                submission.Id,
                student.Id,
                student.FullName,
                submission.SubmittedAtUtc,
                submission.Status,
                submission.Note)
        ).ToListAsync(cancellationToken);
        return new ServiceResult<HomeworkSubmissionListResponse>.Success(new HomeworkSubmissionListResponse(items));
    }

    public async Task<ServiceResult<HomeworkSubmissionResponse>> ReviewAsync(
        Guid assignmentId,
        Guid submissionId,
        string? note,
        CancellationToken cancellationToken)
    {
        var denied = DenyTeacher();
        if (denied is not null)
            return new ServiceResult<HomeworkSubmissionResponse>.Fail(denied.Status, denied.Type, denied.Title, denied.Detail);

        var trimmed = note?.Trim();
        if (trimmed is { Length: > 2000 })
        {
            return Fail<HomeworkSubmissionResponse>(
                StatusCodes.Status400BadRequest,
                "The note is too long.",
                "A review note can contain at most 2000 characters.");
        }

        var schoolId = actor.Context!.SchoolId;
        var owned = await db.HomeworkAssignments.AsNoTracking().AnyAsync(
            item => item.Id == assignmentId && item.TeacherUserId == actor.UserId && item.SchoolId == schoolId,
            cancellationToken);
        if (!owned)
            return Fail<HomeworkSubmissionResponse>(StatusCodes.Status404NotFound, "Not Found", "The homework was not found.");

        var submission = await db.HomeworkSubmissions.FirstOrDefaultAsync(
            item => item.Id == submissionId && item.AssignmentId == assignmentId && item.SchoolId == schoolId,
            cancellationToken);
        if (submission is null)
            return Fail<HomeworkSubmissionResponse>(StatusCodes.Status404NotFound, "Not Found", "The submission was not found.");

        submission.Status = "reviewed";
        if (!string.IsNullOrEmpty(trimmed))
            submission.Note = trimmed;
        await db.SaveChangesAsync(cancellationToken);

        var studentName = await db.Students.AsNoTracking()
            .Where(student => student.Id == submission.StudentId)
            .Select(student => student.FullName)
            .FirstAsync(cancellationToken);
        return new ServiceResult<HomeworkSubmissionResponse>.Success(new HomeworkSubmissionResponse(
            submission.Id,
            submission.StudentId,
            studentName,
            submission.SubmittedAtUtc,
            submission.Status,
            submission.Note));
    }

    private async Task<List<HomeworkRow>> LoadStudentRowsAsync(Guid studentId, Guid schoolId, CancellationToken cancellationToken)
    {
        var assignments = await (
            from enrollment in db.Enrollments.AsNoTracking()
            join year in db.AcademicYears.AsNoTracking() on enrollment.AcademicYearId equals year.Id
            join school in db.Schools.AsNoTracking() on enrollment.SchoolId equals school.Id
            join assignment in db.HomeworkAssignments.AsNoTracking() on enrollment.ClassSectionId equals assignment.ClassSectionId
            join subject in db.Subjects.AsNoTracking() on assignment.SubjectId equals subject.Id
            join section in db.ClassSections.AsNoTracking() on assignment.ClassSectionId equals section.Id
            join grade in db.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
            join teacher in db.Users.AsNoTracking() on assignment.TeacherUserId equals teacher.Id
            where enrollment.StudentId == studentId && enrollment.SchoolId == schoolId && year.IsCurrent
            orderby assignment.DueOn, assignment.CreatedAtUtc
            select new
            {
                assignment.Id,
                assignment.Title,
                assignment.Description,
                assignment.Instructions,
                SubjectName = subject.Name,
                ClassName = section.Name,
                GradeName = grade.Name,
                TeacherName = teacher.FullName,
                assignment.DueOn,
                school.TimeZoneId
            }).ToListAsync(cancellationToken);

        var ids = assignments.Select(item => item.Id).ToArray();
        var submissions = await db.HomeworkSubmissions.AsNoTracking()
            .Where(item => item.StudentId == studentId && ids.Contains(item.AssignmentId))
            .Select(item => new { item.AssignmentId, item.Status, item.Note })
            .ToListAsync(cancellationToken);

        return assignments.Select(item =>
        {
            var submission = submissions.FirstOrDefault(row => row.AssignmentId == item.Id);
            return new HomeworkRow(
                item.Id,
                item.Title,
                item.Description,
                item.Instructions,
                item.SubjectName,
                item.ClassName,
                item.GradeName,
                item.TeacherName ?? "",
                item.DueOn,
                item.TimeZoneId,
                submission?.Status,
                submission?.Note);
        }).ToList();
    }

    private async Task<List<HomeworkRow>> LoadTeacherRowsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        var assignments = await (
            from assignment in db.HomeworkAssignments.AsNoTracking()
            join school in db.Schools.AsNoTracking() on assignment.SchoolId equals school.Id
            join subject in db.Subjects.AsNoTracking() on assignment.SubjectId equals subject.Id
            join section in db.ClassSections.AsNoTracking() on assignment.ClassSectionId equals section.Id
            join grade in db.GradeLevels.AsNoTracking() on section.GradeLevelId equals grade.Id
            join teacher in db.Users.AsNoTracking() on assignment.TeacherUserId equals teacher.Id
            where assignment.TeacherUserId == actor.UserId && assignment.SchoolId == schoolId
            orderby assignment.DueOn descending, assignment.CreatedAtUtc descending
            select new
            {
                assignment.Id,
                assignment.Title,
                assignment.Description,
                assignment.Instructions,
                SubjectName = subject.Name,
                ClassName = section.Name,
                GradeName = grade.Name,
                TeacherName = teacher.FullName,
                assignment.ClassSectionId,
                assignment.DueOn,
                school.TimeZoneId
            }).ToListAsync(cancellationToken);

        var assignmentIds = assignments.Select(item => item.Id).ToArray();
        var submitted = assignmentIds.Length == 0
            ? []
            : await db.HomeworkSubmissions.AsNoTracking()
                .Where(item => assignmentIds.Contains(item.AssignmentId))
                .GroupBy(item => item.AssignmentId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
        var classIds = assignments.Select(item => item.ClassSectionId).Distinct().ToArray();
        var enrolled = classIds.Length == 0
            ? []
            : await (
                from enrollment in db.Enrollments.AsNoTracking()
                join year in db.AcademicYears.AsNoTracking() on enrollment.AcademicYearId equals year.Id
                where classIds.Contains(enrollment.ClassSectionId) && year.IsCurrent
                group enrollment by enrollment.ClassSectionId into students
                select new { students.Key, Count = students.Count() }
            ).ToListAsync(cancellationToken);

        return assignments.Select(item => new HomeworkRow(
            item.Id,
            item.Title,
            item.Description,
            item.Instructions,
            item.SubjectName,
            item.ClassName,
            item.GradeName,
            item.TeacherName ?? "",
            item.DueOn,
            item.TimeZoneId,
            null,
            null,
            submitted.FirstOrDefault(row => row.Key == item.Id)?.Count ?? 0,
            enrolled.FirstOrDefault(row => row.Key == item.ClassSectionId)?.Count ?? 0)).ToList();
    }

    private static HomeworkItemResponse ToItem(HomeworkRow row, DateOnly today)
    {
        var status = row.SubmissionStatus switch
        {
            "submitted" or "reviewing" or "reviewed" => row.SubmissionStatus,
            _ when row.DueOn < today => "closed",
            _ => "awaiting"
        };
        return new HomeworkItemResponse(
            row.Id,
            row.Title,
            row.Description,
            row.Instructions,
            row.SubjectName,
            row.ClassName,
            row.GradeName,
            row.TeacherName,
            row.DueOn,
            status,
            row.Note,
            row.SubmittedCount,
            row.EnrolledCount);
    }

    private async Task<ServiceResult<HomeworkListResponse>.Fail?> DenyStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
            return Fail<HomeworkListResponse>(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.");
        if (actor.MustChangePassword)
        {
            return Fail<HomeworkListResponse>(
                StatusCodes.Status403Forbidden,
                "Password change required",
                "Change the temporary password before continuing.");
        }

        if (actor.Context is null || actor.Context.Role is not (AppRole.Student or AppRole.Parent))
        {
            return Fail<HomeworkListResponse>(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "A student or parent context is required.");
        }

        if (actor.Context.StudentId != studentId)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = actor.UserId,
                SchoolId = actor.Context.SchoolId,
                Role = RoleNames.ToName(actor.Context.Role),
                Action = "student.access_denied",
                EntityName = "student",
                EntityId = studentId,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(cancellationToken);
            return Fail<HomeworkListResponse>(StatusCodes.Status404NotFound, "Not Found", "The student was not found.");
        }

        return null;
    }

    private ServiceResult<HomeworkListResponse>.Fail? DenyTeacher()
    {
        if (!actor.IsAuthenticated)
            return Fail<HomeworkListResponse>(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.");
        if (actor.MustChangePassword)
        {
            return Fail<HomeworkListResponse>(
                StatusCodes.Status403Forbidden,
                "Password change required",
                "Change the temporary password before continuing.");
        }

        if (actor.Context is null || actor.Context.Role != AppRole.Teacher)
        {
            return Fail<HomeworkListResponse>(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "A teacher context is required.");
        }

        return null;
    }

    private static ServiceResult<T>.Fail? ValidateText<T>(string title, string description, string instructions, DateOnly dueOn)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            return Fail<T>(StatusCodes.Status400BadRequest, "The title is not valid.", "A homework title is required and can contain at most 200 characters.");
        if (description.Trim().Length > 4000 || instructions.Trim().Length > 4000)
            return Fail<T>(StatusCodes.Status400BadRequest, "The text is too long.", "The description and instructions can contain at most 4000 characters.");
        if (dueOn == default)
            return Fail<T>(StatusCodes.Status400BadRequest, "The due date is required.", "Choose the date the homework is due.");
        return null;
    }

    private static ServiceResult<T>.Fail Fail<T>(int status, string title, string detail)
    {
        var type = status switch
        {
            StatusCodes.Status401Unauthorized => ProblemResponses.Unauthorized,
            StatusCodes.Status403Forbidden when title == "Password change required" => ProblemResponses.PasswordChange,
            StatusCodes.Status403Forbidden => ProblemResponses.Forbidden,
            StatusCodes.Status404NotFound => ProblemResponses.NotFound,
            _ => "urn:smartschool:validation"
        };
        return new ServiceResult<T>.Fail(status, type, title, detail);
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

    private sealed record HomeworkRow(
        Guid Id,
        string Title,
        string Description,
        string Instructions,
        string SubjectName,
        string ClassName,
        string GradeName,
        string TeacherName,
        DateOnly DueOn,
        string TimeZoneId,
        string? SubmissionStatus,
        string? Note,
        int SubmittedCount = 0,
        int EnrolledCount = 0);
}
