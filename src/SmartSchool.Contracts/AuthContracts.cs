using FluentValidation;
using SmartSchool.Core;

namespace SmartSchool.Contracts;

public sealed class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = "";
}

public sealed class LogoutRequest
{
    public string RefreshToken { get; set; } = "";
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class SelectContextRequest
{
    public Guid SchoolId { get; set; }
    public string Role { get; set; } = "";
    public Guid? StudentId { get; set; }
}

public sealed record MembershipResponse(Guid SchoolId, string SchoolName, string Role);

public sealed record LinkedStudentResponse(Guid StudentId, Guid SchoolId, string SchoolName, string FullName, string Role);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    bool MustChangePassword,
    IReadOnlyList<MembershipResponse> Memberships,
    IReadOnlyList<LinkedStudentResponse> Students);

public sealed record MeResponse(
    Guid UserId,
    string Username,
    bool MustChangePassword,
    IReadOnlyList<MembershipResponse> Memberships,
    ContextResponse? Context,
    IReadOnlyList<LinkedStudentResponse> Students,
    string? FullName,
    string? Email = null,
    string? Phone = null);

public sealed record ContextResponse(Guid ContextId, Guid SchoolId, string Role, Guid? StudentId);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) || RoleNames.TryParse(role, out _))
            .WithMessage("Role must be student, parent, teacher, supervisor, or school_admin.");
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(200);
    }
}

public sealed class SelectContextRequestValidator : AbstractValidator<SelectContextRequest>
{
    public SelectContextRequestValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();
        RuleFor(x => x.Role).Must(role => RoleNames.TryParse(role, out _))
            .WithMessage("Role must be student, parent, teacher, supervisor, or school_admin.");
        RuleFor(x => x.StudentId)
            .NotEmpty()
            .When(x => RoleNames.Normalize(x.Role) is RoleNames.Student or RoleNames.Parent)
            .WithMessage("A student id is required for the student and parent roles.");
    }
}
