namespace SmartSchool.Core;

public sealed class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PreferredLocale { get; set; } = "ar";
    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool MustChangePassword { get; set; }
    public int TokenVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PasswordChangedAtUtc { get; set; }
}

public sealed class School
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "Africa/Cairo";
    public string DefaultLocale { get; set; } = "ar";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Branch
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class UserMembership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SchoolId { get; set; }
    public AppRole Role { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Student
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? UserId { get; set; }
    public string StudentCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Guardian
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GuardianStudent
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid GuardianId { get; set; }
    public Guid StudentId { get; set; }
    public string Relationship { get; set; } = "";
    public bool CanViewFees { get; set; }
    public bool CanRequestLeave { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid? ContextId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

public sealed class UserContext
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SchoolId { get; set; }
    public AppRole Role { get; set; }
    public Guid? StudentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? SchoolId { get; set; }
    public string? Role { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public Guid? EntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AcademicYear
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Term
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GradeLevel
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ClassSection
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid BranchId { get; set; }
    public Guid GradeLevelId { get; set; }
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Subject
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Enrollment
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Guid ClassSectionId { get; set; }
    public Guid AcademicYearId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class TeacherClassAssignment
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public Guid ClassSectionId { get; set; }
    public Guid SubjectId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class TimetableEntry
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassSectionId { get; set; }
    public Guid SubjectId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
    public string? Room { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AttendanceSession
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassSectionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class HomeworkAssignment
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassSectionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Instructions { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class HomeworkSubmission
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid StudentId { get; set; }
    public string Status { get; set; } = "submitted";
    public string? Note { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

public sealed class SchoolTrip
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public int StudentCount { get; set; }
    public string SupervisorName { get; set; } = "";
    public DateOnly TripOn { get; set; }
    public string Status { get; set; } = "current";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AttendanceRecord
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    public AttendanceMark Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
