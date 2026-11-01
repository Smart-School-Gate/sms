using Microsoft.AspNetCore.Mvc;

namespace SmartSchool.Api.Hosting;

public static class ProblemResponses
{
    public const string Unauthorized = "urn:smartschool:unauthorized";
    public const string Forbidden = "urn:smartschool:forbidden";
    public const string NotFound = "urn:smartschool:not-found";
    public const string PasswordChange = "urn:smartschool:password-change-required";
    public const string RateLimit = "urn:smartschool:rate-limit";

    public static IResult Result(int status, string type, string title, string detail) =>
        Results.Problem(detail: detail, statusCode: status, title: title, type: type);

    public static Task WriteAsync(HttpContext http, int status, string type, string title, string detail)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = type
        });
    }
}

public abstract record ServiceResult<T>
{
    public sealed record Success(T Value) : ServiceResult<T>;
    public sealed record Fail(int Status, string Type, string Title, string Detail) : ServiceResult<T>;

    public IResult ToHttpResult() => this switch
    {
        Success success => Results.Ok(success.Value),
        Fail fail => ProblemResponses.Result(fail.Status, fail.Type, fail.Title, fail.Detail),
        _ => ProblemResponses.Result(500, "urn:smartschool:internal", "Internal Server Error", "The request could not be completed.")
    };
}
