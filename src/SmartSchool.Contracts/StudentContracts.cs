namespace SmartSchool.Contracts;

public sealed record NamedId(Guid Id, string Name);

public sealed record StudentProfileResponse(
    Guid StudentId,
    string FullName,
    string StudentCode,
    DateOnly? DateOfBirth,
    string? Email,
    string? Phone,
    NamedId School,
    NamedId Branch,
    NamedId? GradeLevel,
    NamedId? ClassSection,
    NamedId? AcademicYear);

public sealed record AttendanceSummaryResponse(int AttendedCount, int SessionCount, decimal Fraction);

public sealed record TimetableSlotResponse(string SubjectName, string StartsAt, string EndsAt, string? Room);

public sealed record DeadlineResponse(string Kind, Guid Id, string Title, DateTime DueAtUtc);

public sealed record StudentHomeResponse(
    Guid StudentId,
    string FullName,
    AttendanceSummaryResponse Attendance,
    IReadOnlyList<TimetableSlotResponse> Timetable,
    IReadOnlyList<DeadlineResponse> Deadlines,
    string? GradeName = null,
    string ViewerName = "");

public sealed record AttendanceRecordResponse(DateOnly Date, string Status);

public sealed record StudentAttendanceResponse(
    Guid StudentId,
    DateOnly From,
    DateOnly To,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount,
    int SessionCount,
    decimal Fraction,
    IReadOnlyList<AttendanceRecordResponse> Records);

public sealed record TimetableEntryResponse(string DayOfWeek, string SubjectName, string StartsAt, string EndsAt, string? Room);

public sealed record StudentTimetableResponse(
    Guid StudentId,
    Guid? ClassSectionId,
    IReadOnlyList<TimetableEntryResponse> Entries);
