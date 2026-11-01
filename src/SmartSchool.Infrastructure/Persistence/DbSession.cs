namespace SmartSchool.Infrastructure.Persistence;

public interface IDbSession
{
    Guid UserId { get; }
    Guid SchoolId { get; }
    bool HasUser { get; }
    bool HasSchool { get; }
    void SetUser(Guid userId);
    void SetSchool(Guid schoolId);
}

public sealed class DbSession : IDbSession
{
    public Guid UserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public bool HasUser => UserId != Guid.Empty;
    public bool HasSchool => SchoolId != Guid.Empty;
    public void SetUser(Guid userId) => UserId = userId;
    public void SetSchool(Guid schoolId) => SchoolId = schoolId;
}

public static class DbSessionAmbient
{
    private static readonly AsyncLocal<IDbSession?> Current = new();

    public static IDbSession? Session => Current.Value;

    public static IDisposable Push(IDbSession session)
    {
        var prior = Current.Value;
        Current.Value = session;
        return new Popper(prior);
    }

    private sealed class Popper(IDbSession? prior) : IDisposable
    {
        public void Dispose() => Current.Value = prior;
    }
}
