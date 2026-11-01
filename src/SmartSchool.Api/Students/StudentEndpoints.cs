using SmartSchool.Contracts;

namespace SmartSchool.Api.Students;

public static class StudentEndpoints
{
    public static RouteGroupBuilder MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var students = app.MapGroup("/api/v1/students")
            .WithTags("Students")
            .RequireAuthorization();

        students.MapGet("/{studentId:guid}/profile", async (Guid studentId, StudentService service, CancellationToken cancellationToken) =>
            (await service.ProfileAsync(studentId, cancellationToken)).ToHttpResult())
            .WithName("GetStudentProfile")
            .WithDescription("Returns the authorized student or linked child. Homework, fees, and transport are not part of this response.");

        students.MapGet("/{studentId:guid}/home", async (Guid studentId, StudentService service, CancellationToken cancellationToken) =>
            (await service.HomeAsync(studentId, cancellationToken)).ToHttpResult())
            .WithName("GetStudentHome")
            .WithDescription("Returns the student name, attendance fraction, and today's timetable. Deadlines stay empty until homework and exams exist.");

        students.MapGet("/{studentId:guid}/attendance", async (
            Guid studentId,
            DateOnly? from,
            DateOnly? to,
            StudentService service,
            CancellationToken cancellationToken) =>
            (await service.AttendanceAsync(studentId, from, to, cancellationToken)).ToHttpResult())
            .WithName("GetStudentAttendance");

        students.MapGet("/{studentId:guid}/timetable", async (Guid studentId, StudentService service, CancellationToken cancellationToken) =>
            (await service.TimetableAsync(studentId, cancellationToken)).ToHttpResult())
            .WithName("GetStudentTimetable");

        students.MapGet("/{studentId:guid}/homework", async (Guid studentId, HomeworkService service, CancellationToken cancellationToken) =>
            (await service.ListForStudentAsync(studentId, cancellationToken)).ToHttpResult())
            .WithName("GetStudentHomework");

        students.MapPost("/{studentId:guid}/homework/{assignmentId:guid}/submission", async (
            Guid studentId,
            Guid assignmentId,
            SubmitHomeworkRequest request,
            HomeworkService service,
            CancellationToken cancellationToken) =>
            (await service.SubmitAsync(studentId, assignmentId, request.Note, cancellationToken)).ToHttpResult())
            .WithName("SubmitStudentHomework");

        return students;
    }
}
