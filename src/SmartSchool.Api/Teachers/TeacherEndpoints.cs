using SmartSchool.Api.Students;
using SmartSchool.Contracts;

namespace SmartSchool.Api.Teachers;

public static class TeacherEndpoints
{
    public static RouteGroupBuilder MapTeacherEndpoints(this IEndpointRouteBuilder app)
    {
        var teachers = app.MapGroup("/api/v1/teacher")
            .WithTags("Teacher")
            .RequireAuthorization();

        teachers.MapGet("/home", async (TeacherService service, CancellationToken cancellationToken) =>
            (await service.HomeAsync(cancellationToken)).ToHttpResult())
            .WithName("GetTeacherHome")
            .WithDescription("Returns the signed-in teacher name and lessons for classes assigned to that teacher.");

        teachers.MapGet("/schedule", async (TeacherService service, CancellationToken cancellationToken) =>
            (await service.ScheduleAsync(cancellationToken)).ToHttpResult())
            .WithName("GetTeacherSchedule")
            .WithDescription("Returns timetable rows for classes and subjects assigned to the signed-in teacher.");

        teachers.MapGet("/attendance", async (DateOnly? date, TeacherService service, CancellationToken cancellationToken) =>
            (await service.AttendanceAsync(date, cancellationToken)).ToHttpResult())
            .WithName("GetTeacherAttendance")
            .WithDescription("Returns enrolled students and marks for classes assigned to the signed-in teacher.");

        teachers.MapGet("/classes", async (HomeworkService service, CancellationToken cancellationToken) =>
            (await service.ClassesAsync(cancellationToken)).ToHttpResult())
            .WithName("GetTeacherClasses")
            .WithDescription("Returns the classes and subjects assigned to the signed-in teacher.");

        teachers.MapGet("/homework", async (HomeworkService service, CancellationToken cancellationToken) =>
            (await service.ListForTeacherAsync(cancellationToken)).ToHttpResult())
            .WithName("GetTeacherHomework");

        teachers.MapPost("/homework", async (CreateHomeworkRequest request, HomeworkService service, CancellationToken cancellationToken) =>
            (await service.CreateAsync(request, cancellationToken)).ToHttpResult())
            .WithName("CreateTeacherHomework");

        teachers.MapPut("/homework/{assignmentId:guid}", async (
            Guid assignmentId,
            UpdateHomeworkRequest request,
            HomeworkService service,
            CancellationToken cancellationToken) =>
            (await service.UpdateAsync(assignmentId, request, cancellationToken)).ToHttpResult())
            .WithName("UpdateTeacherHomework");

        teachers.MapGet("/homework/{assignmentId:guid}/submissions", async (
            Guid assignmentId,
            HomeworkService service,
            CancellationToken cancellationToken) =>
            (await service.SubmissionsAsync(assignmentId, cancellationToken)).ToHttpResult())
            .WithName("GetTeacherHomeworkSubmissions");

        teachers.MapPut("/homework/{assignmentId:guid}/submissions/{submissionId:guid}", async (
            Guid assignmentId,
            Guid submissionId,
            ReviewHomeworkRequest request,
            HomeworkService service,
            CancellationToken cancellationToken) =>
            (await service.ReviewAsync(assignmentId, submissionId, request.Note, cancellationToken)).ToHttpResult())
            .WithName("ReviewTeacherHomeworkSubmission");

        teachers.MapDelete("/homework/{assignmentId:guid}", async (
            Guid assignmentId,
            HomeworkService service,
            CancellationToken cancellationToken) =>
            (await service.DeleteAsync(assignmentId, cancellationToken)).ToHttpResult())
            .WithName("DeleteTeacherHomework");

        teachers.MapPut("/classes/{classSectionId:guid}/attendance", async (
            Guid classSectionId,
            TeacherAttendanceSaveRequest request,
            TeacherService service,
            CancellationToken cancellationToken) =>
            (await service.SaveAttendanceAsync(classSectionId, request, cancellationToken)).ToHttpResult())
            .WithName("SaveTeacherAttendance")
            .WithDescription("Creates or updates attendance for students enrolled in a class assigned to the signed-in teacher.");

        return teachers;
    }
}
