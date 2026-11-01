using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Api.Tests;
using SmartSchool.Modules.Homework;

namespace SmartSchool.ModuleSelection.Tests;

[Collection(PostgresCollection.Name)]
public sealed class HomeworkModuleHostTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public HomeworkModuleHostTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.AppConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Enabled_host_registers_the_probe_and_keeps_foundation_routes()
    {
        var apiAssembly = typeof(Program).Assembly;
        Assert.Contains(
            apiAssembly.GetReferencedAssemblies(),
            reference => reference.Name == "SmartSchool.Modules.Homework");
        Assert.NotNull(_factory.Services.GetService<HomeworkModuleMarker>());

        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>();
        Assert.Contains(endpoints.Endpoints, endpoint => endpoint.Metadata.GetMetadata<HomeworkModuleMarker>() is not null);

        using var registration = await _client.GetAsync(HomeworkModule.RegistrationPath);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var probe = await registration.Content.ReadFromJsonAsync<HomeworkModuleRegistrationResponse>();
        Assert.NotNull(probe);
        Assert.Equal(HomeworkModule.Name, probe.Module);
        Assert.Equal("registered", probe.State);

        using var business = await _client.GetAsync("/api/v1/homework");
        Assert.Equal(HttpStatusCode.NotFound, business.StatusCode);

        using var live = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "student.a",
            password = "StudentPass-123",
            role = "student"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
