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
        builder.Services.AddSingleton<EbayClient>();
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
        Assert.Equal("https://api.ebay.com/buy/browse/v1/item_summary/search?q=" + Uri.EscapeDataString(query) + "&limit=12", handler.Url);
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
        Assert.Equal("Check the backend eBay App ID, Cert ID, and environment configuration.", problem.RootElement.GetProperty("detail").GetString());
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

    [Theory]
    [InlineData("Production", "api.ebay.com")]
    [InlineData("Sandbox", "api.sandbox.ebay.com")]
    public async Task CredentialsObtainAndReuseToken(string environment, string host)
    {
        using var handler = new OAuthHandler(host);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ebay:ClientId"] = "client-id",
            ["Ebay:ClientSecret"] = "client-secret",
            ["Ebay:Environment"] = environment
        }).Build();
        using var ebay = new EbayClient(new StubClientFactory(handler), configuration);
        Assert.Equal("{}", await ebay.SearchAsync("truck", default));
        Assert.Equal("{}", await ebay.SearchAsync("gps", default));
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(2, handler.SearchCalls);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("-price")]
    [InlineData("newlyListed")]
    public async Task SortAndCombinedFiltersAreForwarded(string sort)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products?q=truck&sort=" + sort + "&freeShipping=true&returnsAccepted=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://api.ebay.com/buy/browse/v1/item_summary/search?q=truck&limit=12&sort=" + sort +
            "&filter=" + Uri.EscapeDataString("maxDeliveryCost:0,returnsAccepted:true"), handler.Url);
    }

    [Theory]
    [InlineData("sort=popularity")]
    [InlineData("sort=invalid")]
    public async Task UnsupportedOptionsAreRejected(string options)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products?q=truck&" + options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ProductDetailUsesEncodedItemIdAndReturnsListing()
    {
        const string body = """{"itemId":"v1|123|0","description":"Description"}""";
        using var handler = new StubHandler(HttpStatusCode.OK, body);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products/detail?itemId=v1%7C123%7C0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal("https://api.ebay.com/buy/browse/v1/item/v1%7C123%7C0", handler.Url);
        Assert.Equal("Bearer test-token", handler.Authorization);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(500, 502)]
    public async Task ProductDetailMapsUpstreamFailures(int upstream, int expected)
    {
        using var handler = new StubHandler((HttpStatusCode)upstream);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products/detail?itemId=v1%7C123%7C0");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/products/detail")]
    [InlineData("/api/products/detail?itemId=")]
    [InlineData("/api/products/detail?itemId=%20")]
    public async Task MissingDetailIdIsRejectedBeforeCallingEbay(string url)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task InvalidUpstreamDetailReturnsBadGateway(string body)
    {
        using var handler = new StubHandler(HttpStatusCode.OK, body);
        await using var app = await StartApp(handler);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/products/detail?itemId=v1%7C123%7C0");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private sealed class OAuthHandler(string host) : HttpMessageHandler
    {
        public int TokenCalls { get; private set; }
        public int SearchCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(host, request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/identity/v1/oauth2/token")
            {
                TokenCalls++;
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret")), request.Headers.Authorization!.ToString());
                Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                Assert.Contains("grant_type=client_credentials", body);
                Assert.Contains("scope=https%3A%2F%2Fapi.ebay.com%2Foauth%2Fapi_scope", body);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"generated-token","expires_in":7200}""") };
            }
            SearchCalls++;
            Assert.Equal("Bearer generated-token", request.Headers.Authorization!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
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
