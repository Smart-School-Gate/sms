using SmartSchool.Core;

namespace SmartSchool.Api.Auth;

public sealed class RequestActor
{
    public bool IsAuthenticated { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public UserContext? Context { get; set; }
}
