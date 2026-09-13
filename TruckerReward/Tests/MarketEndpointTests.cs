using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class MarketEndpointTests
{
    private static async Task<WebApplication> StartApp(StubHandler handler, string? token = "test-token")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ebay:AccessToken"] = token });
        builder.Services.AddSingleton<IHttpClientFactory>(new StubClientFactory(handler));
        var app = builder.Build();
        app.MapMarketEndpoints();
        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("truck GPS")]
    [InlineData("tools & parts/+?=雪")]
    public async Task FetchSendsEncodedQueryAndEbayHeadersAndReturnsJson(string query)
    {
        const string json = """{"itemSummaries":[{"title":"Truck GPS"}]}""";
        using var handler = new StubHandler(HttpStatusCode.OK, json);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products?q=" + Uri.EscapeDataString(query));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(json, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://api.ebay.com/buy/browse/v1/item_summary/search?q=" + Uri.EscapeDataString(query) + "&limit=10", handler.Url);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.Equal("EBAY_US", handler.Marketplace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingTokenReturnsProblemWithoutCallingEbay(string? token)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        await using var app = await StartApp(handler, token);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products?q=truck");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("The eBay access token is not configured.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task EbayFailureReturnsBadGateway(int status)
    {
        using var handler = new StubHandler((HttpStatusCode)status, "private upstream error");
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products?q=truck");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal($"eBay returned status {status}.", problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("private upstream error", body);
    }

    [Fact]
    public async Task MissingQueryReturnsBadRequestWithoutCallingEbay()
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class StubClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? Marketplace { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri?.AbsoluteUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Marketplace = request.Headers.GetValues("X-EBAY-C-MARKETPLACE-ID").Single();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
