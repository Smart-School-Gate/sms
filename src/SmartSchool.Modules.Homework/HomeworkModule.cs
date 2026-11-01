using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSchool.Modules.Homework;

/// <summary>
/// Registration probe for the optional Homework project. Not a homework business API.
/// </summary>
public static class HomeworkModule
{
    public const string Name = "homework";

    public const string RegistrationPath = "/api/v1/modules/homework/registration";

    public static IServiceCollection AddHomeworkModule(this IServiceCollection services)
    {
        services.AddSingleton<HomeworkModuleMarker>();
        return services;
    }

    public static IEndpointRouteBuilder MapHomeworkModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RegistrationPath, (HomeworkModuleMarker marker) =>
                Results.Ok(new HomeworkModuleRegistrationResponse(marker.Name, "registered")))
            .WithName("HomeworkModuleRegistration")
            .WithTags("ModuleDiagnostics")
            .WithMetadata(new HomeworkModuleMarker())
            .ExcludeFromDescription();
        return endpoints;
    }
}

public sealed class HomeworkModuleMarker
{
    public string Name => HomeworkModule.Name;
}

public sealed record HomeworkModuleRegistrationResponse(string Module, string State);
