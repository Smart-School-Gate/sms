using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Auth;

public sealed class AuthService(
    AppDbContext db,
    IDbSession session,
    PasswordHasher<User> passwords,
    TokenService tokens,
    TimeProvider time)
{
    private readonly string _dummyHash = passwords.HashPassword(new User(), "not-a-real-password");
    private const string InvalidLogin = "The username or password is incorrect.";

    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Username == username, cancellationToken);
        var hash = user?.PasswordHash ?? _dummyHash;
        var verification = passwords.VerifyHashedPassword(user ?? new User(), hash, request.Password);
        var passwordMatches = user is not null
            && user.Status == UserStatus.Active
            && verification is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        if (!passwordMatches || user is null)
            return Unauthorized<AuthResponse>(InvalidLogin);

        session.SetUser(user.Id);
        var memberships = await LoadMembershipsAsync(user.Id, cancellationToken);
        var requested = request.Role.Trim();
        if (requested.Length == 0)
        {
            if (!memberships.Any(x => x.Role is RoleNames.Student or RoleNames.Parent or RoleNames.Teacher or RoleNames.Supervisor))
                return Forbidden<AuthResponse>("The requested role is not assigned.");
        }
        else if (!RoleNames.TryParse(requested, out var requestedRole)
            || memberships.All(x => x.Role != RoleNames.ToName(requestedRole)))
        {
            return Forbidden<AuthResponse>("The requested role is not assigned.");
        }

        var response = await IssueAsync(user, contextId: null, memberships, cancellationToken);
        AddAudit(user.Id, null, null, "auth.login", "user", user.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<AuthResponse>.Success(response);
    }

    public async Task<ServiceResult<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(
            x => x.TokenHash == TokenService.Hash(refreshToken),
            cancellationToken);
        if (existing is null)
            return Unauthorized<AuthResponse>("The refresh token is not valid.");

        if (existing.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Unauthorized<AuthResponse>("The refresh token is not valid.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (existing.ExpiresAtUtc <= now)
            return Unauthorized<AuthResponse>("The refresh token is not valid.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == existing.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
            return Unauthorized<AuthResponse>("The refresh token is not valid.");

        session.SetUser(user.Id);
        var memberships = await LoadMembershipsAsync(user.Id, cancellationToken);
        if (memberships.Count == 0)
            return Unauthorized<AuthResponse>("The refresh token is not valid.");

        Guid? contextId = null;
        if (existing.ContextId is Guid selected)
        {
            var context = await db.UserContexts.FirstOrDefaultAsync(
                x => x.Id == selected && x.UserId == user.Id && x.RevokedAtUtc == null,
                cancellationToken);
            var membershipStillActive = context is not null
                && memberships.Any(x => x.SchoolId == context.SchoolId && x.Role == RoleNames.ToName(context.Role));
            if (!membershipStillActive || context is null || !await ContextStillLinkedAsync(user.Id, context, cancellationToken))
                return Unauthorized<AuthResponse>("The selected context is no longer valid.");
            contextId = context.Id;
        }

        existing.RevokedAtUtc = now;
        var response = await IssueAsync(user, contextId, memberships, cancellationToken, existing.FamilyId);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<AuthResponse>.Success(response);
    }

    public async Task<ServiceResult<object>> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(
            x => x.TokenHash == TokenService.Hash(refreshToken),
            cancellationToken);
        if (existing is null)
            return new ServiceResult<object>.Success(new { });

        await RevokeFamilyAsync(existing.FamilyId, cancellationToken);
        AddAudit(existing.UserId, null, null, "auth.logout", "user", existing.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<object>.Success(new { });
    }

    public async Task<ServiceResult<AuthResponse>> ChangePasswordAsync(
        RequestActor actor,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
            return Unauthorized<AuthResponse>("Authentication is required.");

        var user = await db.Users.FirstAsync(x => x.Id == actor.UserId, cancellationToken);
        var verification = passwords.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
            return Unauthorized<AuthResponse>("The current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword)
            return Forbidden<AuthResponse>("The new password must be different from the current password.");

        user.PasswordHash = passwords.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = time.GetUtcNow().UtcDateTime;
        user.TokenVersion += 1;
        session.SetUser(user.Id);
        await RevokeUserTokensAsync(user.Id, cancellationToken);
        var memberships = await LoadMembershipsAsync(user.Id, cancellationToken);
        var response = await IssueAsync(user, contextId: null, memberships, cancellationToken);
        AddAudit(user.Id, null, null, "auth.password_changed", "user", user.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<AuthResponse>.Success(response);
    }

    public async Task<ServiceResult<AuthResponse>> SelectContextAsync(
        RequestActor actor,
        SelectContextRequest request,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
            return Unauthorized<AuthResponse>("Authentication is required.");
        if (actor.MustChangePassword)
            return PasswordChange<AuthResponse>();
        if (!RoleNames.TryParse(request.Role, out var role))
            return Forbidden<AuthResponse>("The requested context is not authorized.");

        session.SetUser(actor.UserId);
        var membership = await db.UserMemberships.FirstOrDefaultAsync(
            x => x.UserId == actor.UserId
                && x.SchoolId == request.SchoolId
                && x.Role == role
                && x.Status == MembershipStatus.Active,
            cancellationToken);
        if (membership is null)
            return Forbidden<AuthResponse>("The requested context is not authorized.");

        session.SetSchool(request.SchoolId);
        if (role is AppRole.Student or AppRole.Parent)
        {
            var studentId = request.StudentId ?? Guid.Empty;
            if (!await CanUseStudentAsync(actor.UserId, role, studentId, cancellationToken))
                return Forbidden<AuthResponse>("The requested context is not authorized.");
        }
        else if (request.StudentId is not null)
        {
            return Forbidden<AuthResponse>("The requested context is not authorized.");
        }

        var context = new UserContext
        {
            Id = Guid.NewGuid(),
            UserId = actor.UserId,
            SchoolId = request.SchoolId,
            Role = role,
            StudentId = request.StudentId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        db.UserContexts.Add(context);
        var user = await db.Users.FirstAsync(x => x.Id == actor.UserId, cancellationToken);
        var memberships = await LoadMembershipsAsync(user.Id, cancellationToken);
        var response = await IssueAsync(user, context.Id, memberships, cancellationToken);
        AddAudit(user.Id, request.SchoolId, RoleNames.ToName(role), "auth.context_selected", "user_context", context.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new ServiceResult<AuthResponse>.Success(response);
    }

    public async Task<ServiceResult<MeResponse>> MeAsync(RequestActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated)
            return Unauthorized<MeResponse>("Authentication is required.");

        session.SetUser(actor.UserId);
        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == actor.UserId, cancellationToken);
        var memberships = await LoadMembershipsAsync(actor.UserId, cancellationToken);
        ContextResponse? context = null;
        if (actor.Context is not null)
        {
            context = new ContextResponse(
                actor.Context.Id,
                actor.Context.SchoolId,
                RoleNames.ToName(actor.Context.Role),
                actor.Context.StudentId);
        }

        var students = await LoadLinkedStudentsAsync(actor.UserId, memberships, cancellationToken);
        return new ServiceResult<MeResponse>.Success(new MeResponse(
            user.Id,
            user.Username,
            user.MustChangePassword,
            memberships,
            context,
            students,
            user.FullName,
            user.Email,
            user.Phone));
    }

    private async Task<bool> CanUseStudentAsync(Guid userId, AppRole role, Guid studentId, CancellationToken cancellationToken)
    {
        if (role == AppRole.Student)
        {
            return await db.Students.AnyAsync(x => x.Id == studentId && x.UserId == userId, cancellationToken);
        }

        var guardianId = await db.Guardians
            .Where(x => x.UserId == userId)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return guardianId != Guid.Empty
            && await db.GuardianStudents.AnyAsync(
                x => x.GuardianId == guardianId && x.StudentId == studentId,
                cancellationToken);
    }

    private async Task<bool> ContextStillLinkedAsync(Guid userId, UserContext context, CancellationToken cancellationToken)
    {
        if (context.Role is not (AppRole.Student or AppRole.Parent))
            return true;
        if (context.StudentId is not Guid studentId)
            return false;
        session.SetSchool(context.SchoolId);
        return await CanUseStudentAsync(userId, context.Role, studentId, cancellationToken);
    }

    private async Task<IReadOnlyList<MembershipResponse>> LoadMembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
            from membership in db.UserMemberships.AsNoTracking()
            join school in db.Schools.AsNoTracking() on membership.SchoolId equals school.Id
            where membership.UserId == userId && membership.Status == MembershipStatus.Active
            orderby school.Name, membership.Role
            select new { membership.SchoolId, school.Name, membership.Role }
        ).ToListAsync(cancellationToken);
        return rows
            .Select(x => new MembershipResponse(x.SchoolId, x.Name, RoleNames.ToName(x.Role)))
            .ToArray();
    }

    private async Task<AuthResponse> IssueAsync(
        User user,
        Guid? contextId,
        IReadOnlyList<MembershipResponse> memberships,
        CancellationToken cancellationToken,
        Guid? familyId = null)
    {
        var linkedStudents = await LoadLinkedStudentsAsync(user.Id, memberships, cancellationToken);
        var refresh = TokenService.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId ?? Guid.NewGuid(),
            TokenHash = TokenService.Hash(refresh),
            ContextId = contextId,
            ExpiresAtUtc = tokens.RefreshExpiresAtUtc(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        });
        return new AuthResponse(
            tokens.CreateAccessToken(user.Id, user.TokenVersion, contextId),
            refresh,
            tokens.ExpiresInSeconds,
            user.MustChangePassword,
            memberships,
            linkedStudents);
    }

    private async Task<IReadOnlyList<LinkedStudentResponse>> LoadLinkedStudentsAsync(
        Guid userId,
        IReadOnlyList<MembershipResponse> memberships,
        CancellationToken cancellationToken)
    {
        var previousSchool = session.HasSchool ? session.SchoolId : Guid.Empty;
        var results = new List<LinkedStudentResponse>();
        foreach (var membership in memberships)
        {
            if (!RoleNames.TryParse(membership.Role, out var role) || role is not (AppRole.Student or AppRole.Parent))
                continue;

            session.SetSchool(membership.SchoolId);
            if (role == AppRole.Student)
            {
                var rows = await db.Students.AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .Select(x => new { x.Id, x.FullName })
                    .ToListAsync(cancellationToken);
                results.AddRange(rows.Select(x => new LinkedStudentResponse(
                    x.Id, membership.SchoolId, membership.SchoolName, x.FullName, membership.Role)));
                continue;
            }

            var children = await (
                from guardian in db.Guardians.AsNoTracking()
                join link in db.GuardianStudents.AsNoTracking() on guardian.Id equals link.GuardianId
                join student in db.Students.AsNoTracking() on link.StudentId equals student.Id
                where guardian.UserId == userId
                select new { student.Id, student.FullName }
            ).ToListAsync(cancellationToken);
            results.AddRange(children.Select(x => new LinkedStudentResponse(
                x.Id, membership.SchoolId, membership.SchoolName, x.FullName, membership.Role)));
        }

        session.SetSchool(previousSchool);
        return results;
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var tokensInFamily = await db.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokensInFamily)
            token.RevokedAtUtc = now;
    }

    private async Task RevokeUserTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var active = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
            token.RevokedAtUtc = now;
    }

    private void AddAudit(Guid userId, Guid? schoolId, string? role, string action, string entity, Guid entityId)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            SchoolId = schoolId,
            Role = role,
            Action = action,
            EntityName = entity,
            EntityId = entityId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        });
    }

    private static ServiceResult<T> Unauthorized<T>(string detail) =>
        new ServiceResult<T>.Fail(StatusCodes.Status401Unauthorized, ProblemResponses.Unauthorized, "Unauthorized", detail);

    private static ServiceResult<T> Forbidden<T>(string detail) =>
        new ServiceResult<T>.Fail(StatusCodes.Status403Forbidden, ProblemResponses.Forbidden, "Forbidden", detail);

    private static ServiceResult<T> PasswordChange<T>() =>
        new ServiceResult<T>.Fail(
            StatusCodes.Status403Forbidden,
            ProblemResponses.PasswordChange,
            "Password change required",
            "Change the temporary password before continuing.");
}
