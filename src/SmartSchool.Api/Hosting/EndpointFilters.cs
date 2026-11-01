using FluentValidation;

namespace SmartSchool.Api.Hosting;

public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<T>().FirstOrDefault();
        if (model is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["body"] = ["A request body is required."] },
                type: "urn:smartschool:validation",
                title: "One or more validation errors occurred.");
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
        var result = await validator.ValidateAsync(model, context.HttpContext.RequestAborted);
        if (!result.IsValid)
            return Results.ValidationProblem(
                result.ToDictionary(),
                type: "urn:smartschool:validation",
                title: "One or more validation errors occurred.");

        return await next(context);
    }
}
