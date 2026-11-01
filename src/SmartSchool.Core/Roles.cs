namespace SmartSchool.Core;

public enum AppRole
{
    Student,
    Parent,
    Teacher,
    Supervisor,
    SchoolAdmin
}

public enum UserStatus
{
    Active,
    Disabled
}

public enum MembershipStatus
{
    Active,
    Revoked
}

public enum AttendanceMark
{
    Present,
    Absent,
    Late,
    Excused
}

public static class RoleNames
{
    public const string Student = "student";
    public const string Parent = "parent";
    public const string Teacher = "teacher";
    public const string Supervisor = "supervisor";
    public const string SchoolAdmin = "school_admin";

    public static bool TryParse(string? value, out AppRole role)
    {
        switch (Normalize(value))
        {
            case Student:
                role = AppRole.Student;
                return true;
            case Parent:
                role = AppRole.Parent;
                return true;
            case Teacher:
                role = AppRole.Teacher;
                return true;
            case Supervisor:
                role = AppRole.Supervisor;
                return true;
            case SchoolAdmin:
                role = AppRole.SchoolAdmin;
                return true;
            default:
                role = default;
                return false;
        }
    }

    public static string ToName(AppRole role) => role switch
    {
        AppRole.Student => Student,
        AppRole.Parent => Parent,
        AppRole.Teacher => Teacher,
        AppRole.Supervisor => Supervisor,
        AppRole.SchoolAdmin => SchoolAdmin,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? "";
}
