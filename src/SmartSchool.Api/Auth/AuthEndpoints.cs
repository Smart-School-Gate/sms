using SmartSchool.Api.Hosting;
using SmartSchool.Contracts;

namespace SmartSchool.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Identity");
        auth.MapPost("/login", async (LoginRequest request, AuthService service, CancellationToken cancellationToken) =>
            (await service.LoginAsync(request, cancellationToken)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting("credentials")
            .WithName("Login");

        auth.MapPost("/refresh", async (RefreshRequest request, AuthService service, CancellationToken cancellationToken) =>
            (await service.RefreshAsync(request.RefreshToken, cancellationToken)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .RequireRateLimiting("credentials")
            .WithName("Refresh");

        auth.MapPost("/logout", async (LogoutRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.LogoutAsync(request.RefreshToken, cancellationToken);
            return result is ServiceResult<object>.Success
                ? Results.NoContent()
                : result.ToHttpResult();
        })
            .AddEndpointFilter<ValidationFilter<LogoutRequest>>()
            .WithName("Logout");

        auth.MapPost("/change-password", async (ChangePasswordRequest request, AuthService service, RequestActor actor, CancellationToken cancellationToken) =>
            (await service.ChangePasswordAsync(actor, request, cancellationToken)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<ChangePasswordRequest>>()
            .RequireAuthorization()
            .RequireRateLimiting("credentials")
            .WithName("ChangePassword");

        var me = app.MapGroup("/api/v1/me").WithTags("Identity").RequireAuthorization();
        me.MapGet("/", async (AuthService service, RequestActor actor, CancellationToken cancellationToken) =>
            (await service.MeAsync(actor, cancellationToken)).ToHttpResult())
            .WithName("GetCurrentUser");

        me.MapPut("/context", async (SelectContextRequest request, AuthService service, RequestActor actor, CancellationToken cancellationToken) =>
            (await service.SelectContextAsync(actor, request, cancellationToken)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<SelectContextRequest>>()
            .WithName("SelectContext");

        return auth;
    }
}
