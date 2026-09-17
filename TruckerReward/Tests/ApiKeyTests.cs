using System.Net;
using Xunit;

namespace TruckerReward.Tests;

// the frontend is the only thing meant to call the backend, so when BACKEND_API_KEY is set
// every request except /health has to carry it
public sealed class ApiKeyTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private Task<HttpClient> Start(string? apiKey) =>
        app.Start(b => b.Configuration["BACKEND_API_KEY"] = apiKey);

    [Fact]
    public async Task NoKeyConfiguredMeansEverythingIsOpen()
    {
        var client = await Start(apiKey: null);
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingKeyIsRejected()
    {
        var client = await Start("s3cret");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongKeyIsRejected()
    {
        var client = await Start("s3cret");
        client.DefaultRequestHeaders.Add("X-Api-Key", "guess");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RightKeyIsAccepted()
    {
        var client = await Start("s3cret");
        client.DefaultRequestHeaders.Add("X-Api-Key", "s3cret");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthAndRootStayOpenForBeanstalkChecks()
    {
        var client = await Start("s3cret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
