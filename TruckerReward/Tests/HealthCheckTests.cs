using System.Net;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace TruckerReward.Tests;

public sealed class HealthCheckTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private WebApplication? app;

    private async Task<HttpClient> Start()
    {
        app = FrontendHost.Builder(backend).Build();
        app.MapHealthCheck();
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task HealthyWhenTheBackendIs()
    {
        backend.Routes["GET /health"] = (HttpStatusCode.OK, "{}");
        var response = await (await Start()).GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task UnhealthyWhenTheBackendIsnt()
    {
        backend.Routes["GET /health"] = (HttpStatusCode.ServiceUnavailable, "{}");
        var response = await (await Start()).GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }
}
