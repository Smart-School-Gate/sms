namespace SmartSchool.Contracts;

public sealed record TeacherLessonResponse(
    string Day,
    string Title,
    string ClassName,
    string StartsAt,
    string EndsAt);

public sealed record TeacherScheduleResponse(IReadOnlyList<TeacherLessonResponse> Lessons);

public sealed record TeacherHomeResponse(
    string FullName,
    int TodayLessonCount,
    int TodayLessonTotal,
    IReadOnlyList<TeacherLessonResponse> Lessons);

public sealed record TeacherAttendanceMarkRequest(Guid StudentId, string Status);

public sealed record TeacherAttendanceSaveRequest(DateOnly Date, IReadOnlyList<TeacherAttendanceMarkRequest> Marks);

public sealed record TeacherAttendanceStudentResponse(Guid StudentId, string FullName, string? Status);

public sealed record TeacherAttendanceClassResponse(
    Guid ClassSectionId,
    string ClassName,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount,
    int UnmarkedCount,
    IReadOnlyList<TeacherAttendanceStudentResponse> Students);

public sealed record TeacherAttendanceResponse(
    DateOnly Date,
    IReadOnlyList<TeacherAttendanceClassResponse> Classes);
