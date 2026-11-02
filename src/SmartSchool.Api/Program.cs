using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using SmartSchool.Api.Auth;
using SmartSchool.Api.Chat;
using SmartSchool.Api.Hosting;
using SmartSchool.Api.Students;
using SmartSchool.Api.Supervisors;
using SmartSchool.Api.Teachers;
using SmartSchool.Contracts;
using SmartSchool.Core;
using SmartSchool.Infrastructure.Persistence;
#if INCLUDE_HOMEWORK
using SmartSchool.Modules.Homework;
#endif

if (args.Contains("migrate"))
    return await MigrationRunner.RunAsync();

if (args.Contains("provision-admin"))
    return await ProvisionAdminCommand.RunAsync();

if (args.Contains("prepare-test-school"))
    return await PrepareTestSchoolCommand.RunAsync();

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (string.IsNullOrWhiteSpace(port))
    port = "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var databaseUrl = PostgresConnectionStrings.RequireFromEnvironment();
var connectionString = PostgresConnectionStrings.ToNpgsql(databaseUrl);
var signingKey = builder.Configuration["AUTH_SIGNING_KEY"] ?? "";
if (signingKey.Length < 32 || signingKey.Contains("CHANGE_ME", StringComparison.Ordinal))
    throw new InvalidOperationException("AUTH_SIGNING_KEY must be a random value of at least 32 characters.");

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .PostConfigure(options => options.SigningKey = signingKey)
    .Validate(options => options.AccessTokenMinutes is >= 5 and <= 60, "Access token lifetime must be between 5 and 60 minutes.")
    .Validate(options => options.RefreshTokenDays is >= 1 and <= 30, "Refresh token lifetime must be between 1 and 30 days.")
    .ValidateOnStart();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddSchoolPersistence(connectionString);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ActorLoader>();
builder.Services.AddScoped<RequestActor>();
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<ChatRealtime>();
builder.Services.AddSignalR();
builder.Services.AddScoped<HomeworkService>();
builder.Services.AddScoped<TeacherService>();
builder.Services.AddScoped<SupervisorService>();
#if INCLUDE_HOMEWORK
builder.Services.AddHomeworkModule();
#endif
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                    return;
                await ProblemResponses.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    ProblemResponses.Unauthorized,
                    "Unauthorized",
                    "Authentication is required.");
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                    return;
                await ProblemResponses.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    ProblemResponses.Forbidden,
                    "Forbidden",
                    "You do not have permission to perform this action.");
            }
        };
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Concat(new[]
    {
        "http://localhost:8081",
        "http://127.0.0.1:8081",
        "http://localhost:8091",
        "http://127.0.0.1:8091",
        "http://127.0.0.1:8082"
    })
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];
if (allowedOrigins.Any(origin => origin == "*"))
    throw new InvalidOperationException("CORS must name explicit origins. A wildcard origin is not allowed.");
var development = builder.Environment.IsDevelopment();
if (development || allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("local-web", policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                {
                    if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                        return true;
                    if (!development || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        return false;
                    return uri.Scheme == Uri.UriSchemeHttp
                        && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1");
                })
                .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With", "X-SignalR-User-Agent")
                .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS");
        });
    });
}

var permitLimit = builder.Configuration.GetValue("Auth:LoginPermitLimit", 10);
var windowSeconds = builder.Configuration.GetValue("Auth:LoginWindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        if (context.HttpContext.Response.HasStarted)
            return;
        await ProblemResponses.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            ProblemResponses.RateLimit,
            "Too Many Requests",
            "Too many attempts. Try again later.");
    };
    options.AddPolicy("credentials", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0
            }));
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Smart School API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async http =>
    {
        var exception = http.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SmartSchool.Api");
        logger.LogError("Unhandled {ExceptionType}", exception?.GetType().Name ?? "Exception");
        if (http.Response.HasStarted)
            return;

        if (exception is BadHttpRequestException)
        {
            await ProblemResponses.WriteAsync(
                http,
                StatusCodes.Status400BadRequest,
                "urn:smartschool:validation",
                "Bad Request",
                "The request body is not valid.");
            return;
        }

        await ProblemResponses.WriteAsync(
            http,
            StatusCodes.Status500InternalServerError,
            "urn:smartschool:internal",
            "Internal Server Error",
            "The request could not be completed.");
    });
});

if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();

if (development || allowedOrigins.Length > 0)
    app.UseCors("local-web");

app.UseRateLimiter();
app.Use(async (http, next) =>
{
    var session = http.RequestServices.GetRequiredService<IDbSession>();
    using var scope = DbSessionAmbient.Push(session);
    if (http.Request.Path.StartsWithSegments("/health") || http.Request.Path.StartsWithSegments("/hubs"))
    {
        await next(http);
        return;
    }

    var db = http.RequestServices.GetRequiredService<AppDbContext>();
    await db.Database.OpenConnectionAsync(http.RequestAborted);
    DbRoundTrips.Begin();
    try
    {
        await next(http);
    }
    finally
    {
        var counts = DbRoundTrips.Read();
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SmartSchool.Database")
            .LogInformation(
                "path={Path} queries={Queries} sessionCommands={Sessions}",
                http.Request.Path.Value,
                counts.Queries,
                counts.Sessions);
        await db.Database.CloseConnectionAsync();
    }
});
app.UseAuthentication();
app.Use(async (http, next) =>
{
    var loader = http.RequestServices.GetRequiredService<ActorLoader>();
    if (!await loader.LoadAsync(http, http.RequestAborted))
        return;

    var actor = http.RequestServices.GetRequiredService<RequestActor>();
    if (actor.IsAuthenticated
        && actor.MustChangePassword
        && !http.Request.Path.Equals("/api/v1/auth/change-password", StringComparison.OrdinalIgnoreCase))
    {
        await ProblemResponses.WriteAsync(
            http,
            StatusCodes.Status403Forbidden,
            ProblemResponses.PasswordChange,
            "Password change required",
            "Change the temporary password before continuing.");
        return;
    }

    await next(http);
});
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapStudentEndpoints();
app.MapChatEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapTeacherEndpoints();
app.MapSupervisorEndpoints();
#if INCLUDE_HOMEWORK
app.MapHomeworkModule();
#endif
app.MapFallback(async http =>
{
    if (http.Response.HasStarted)
        return;
    await ProblemResponses.WriteAsync(
        http,
        StatusCodes.Status404NotFound,
        ProblemResponses.NotFound,
        "Not Found",
        "The requested resource was not found.");
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
    ResponseWriter = WriteApiAvailability
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
    ResponseWriter = WriteDatabaseReadiness
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
    ResponseWriter = WriteDatabaseReadiness
});

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("OpenApi:Enabled", false))
    app.MapOpenApi("/openapi/v1.json");

app.Run();
return 0;

static Task WriteApiAvailability(HttpContext http, HealthReport report)
{
    http.Response.ContentType = "application/json";
    http.Response.StatusCode = report.Status == HealthStatus.Healthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;
    return http.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        probe = "live"
    });
}

static Task WriteDatabaseReadiness(HttpContext http, HealthReport report)
{
    http.Response.ContentType = "application/json";
    http.Response.StatusCode = report.Status == HealthStatus.Healthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;
    return http.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        probe = "ready",
        checks = report.Entries.Select(static entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString()
        }).ToArray()
    });
}

public partial class Program;
