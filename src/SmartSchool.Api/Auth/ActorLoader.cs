using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core;
using SmartSchool.Api.Hosting;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Auth;

public sealed class ActorLoader(AppDbContext db, IDbSession session, RequestActor actor)
{
    private static readonly ConcurrentDictionary<string, CachedPrincipal> Fresh = new();

    public async Task<bool> LoadAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var sub = http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
            return true;
        if (!Guid.TryParse(sub, out var userId) || !int.TryParse(http.User.FindFirst("ver")?.Value, out var version))
        {
            await ProblemResponses.WriteAsync(http, StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "The access token is not valid.");
            return false;
        }

        session.SetUser(userId);
        var contextValue = http.User.FindFirst("ctx")?.Value;
        Guid? contextId = null;
        if (!string.IsNullOrEmpty(contextValue))
        {
            if (!Guid.TryParse(contextValue, out var parsedContextId))
            {
                await ProblemResponses.WriteAsync(http, StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "The access token is not valid.");
                return false;
            }

            contextId = parsedContextId;
        }

        var key = $"{userId:N}:{version}:{contextId}";
        if (contextId is Guid cachedContextId && Fresh.TryGetValue(key, out var cached) && cached.UntilTicks > Environment.TickCount64)
        {
            actor.IsAuthenticated = true;
            actor.UserId = userId;
            actor.FullName = cached.FullName;
            actor.MustChangePassword = cached.MustChangePassword;
            session.SetSchool(cached.SchoolId);
            actor.Context = new UserContext
            {
                Id = cachedContextId,
                UserId = userId,
                SchoolId = cached.SchoolId,
                Role = cached.Role,
                StudentId = cached.StudentId,
                CreatedAtUtc = DateTime.UtcNow
            };
            return true;
        }

        var row = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Status,
                user.TokenVersion,
                user.MustChangePassword,
                user.FullName,
                Context = db.UserContexts.AsNoTracking()
                    .Where(item => contextId != null && item.Id == contextId && item.UserId == user.Id && item.RevokedAtUtc == null)
                    .Select(item => new
                    {
                        item.Id,
                        item.UserId,
                        item.SchoolId,
                        item.Role,
                        item.StudentId,
                        item.CreatedAtUtc,
                        MembershipActive = db.UserMemberships.Any(membership =>
                            membership.UserId == user.Id
                            && membership.SchoolId == item.SchoolId
                            && membership.Role == item.Role
                            && membership.Status == MembershipStatus.Active)
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.Status != UserStatus.Active || row.TokenVersion != version)
        {
            await ProblemResponses.WriteAsync(http, StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "The access token is not valid.");
            return false;
        }

        actor.IsAuthenticated = true;
        actor.UserId = userId;
        actor.FullName = row.FullName ?? "";
        actor.MustChangePassword = row.MustChangePassword;

        if (contextId is null)
            return true;
        if (row.Context is null || !row.Context.MembershipActive)
        {
            await ProblemResponses.WriteAsync(http, StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "The selected context is no longer valid.");
            return false;
        }

        var context = new UserContext
        {
            Id = row.Context.Id,
            UserId = row.Context.UserId,
            SchoolId = row.Context.SchoolId,
            Role = row.Context.Role,
            StudentId = row.Context.StudentId,
            CreatedAtUtc = row.Context.CreatedAtUtc
        };
        session.SetSchool(context.SchoolId);
        if (context.Role is AppRole.Student or AppRole.Parent)
        {
            if (context.StudentId is not Guid studentId || !await StillLinkedAsync(userId, context.Role, studentId, cancellationToken))
            {
                await ProblemResponses.WriteAsync(http, StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", "The selected context is no longer valid.");
                return false;
            }
        }

        actor.Context = context;
        Fresh[key] = new CachedPrincipal(
            Environment.TickCount64 + 20_000,
            actor.MustChangePassword,
            actor.FullName,
            context.SchoolId,
            context.Role,
            context.StudentId);
        return true;
    }

    private async Task<bool> StillLinkedAsync(Guid userId, AppRole role, Guid studentId, CancellationToken cancellationToken)
    {
        if (role == AppRole.Student)
            return await db.Students.AsNoTracking().AnyAsync(x => x.Id == studentId && x.UserId == userId, cancellationToken);

        return await (
            from guardian in db.Guardians.AsNoTracking()
            join link in db.GuardianStudents.AsNoTracking() on guardian.Id equals link.GuardianId
            where guardian.UserId == userId && link.StudentId == studentId
            select link.StudentId
        ).AnyAsync(cancellationToken);
    }

    private sealed record CachedPrincipal(
        long UntilTicks,
        bool MustChangePassword,
        string FullName,
        Guid SchoolId,
        AppRole Role,
        Guid? StudentId);
}
