namespace SmartSchool.Api.Supervisors;

public static class SupervisorEndpoints
{
    public static RouteGroupBuilder MapSupervisorEndpoints(this IEndpointRouteBuilder app)
    {
        var supervisors = app.MapGroup("/api/v1/supervisor")
            .WithTags("Supervisor")
            .RequireAuthorization();

        supervisors.MapGet("/home", async (SupervisorService service, CancellationToken cancellationToken) =>
            (await service.HomeAsync(cancellationToken)).ToHttpResult())
            .WithName("GetSupervisorHome")
            .WithDescription("Returns the signed-in supervisor name. Trip rows stay empty until a supervisor assignment exists.");

        return supervisors;
    }
}
