using Bunit;
using FrontEnd.Pages;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductGridTests : TestContext
{
    private readonly StubHandler handler = new();

    public ProductGridTests()
    {
        Services.AddHttpClient("Backend", client => client.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    private IRenderedComponent<Products> RenderCatalog(string json)
    {
        handler.Body = json;
        var component = RenderComponent<Products>();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=status]")));
        return component;
    }
    [Fact]
    public void GridRendersTitlesImagesAndMissingImageFallbacks()
    {
        var component = RenderCatalog("""
            {"itemSummaries":[
              {"title":"Truck GPS","image":{"imageUrl":"https://example.com/gps.jpg"}},
              {"title":"Gloves"},
              {"title":"Boots","image":{"imageUrl":" "}}
            ]}
            """);

        Assert.Equal(3, component.FindAll(".product-grid .product-card").Count);
        Assert.Equal(new[] { "Truck GPS", "Gloves", "Boots" },
            component.FindAll(".product-card h2").Select(element => element.TextContent));
        var image = component.Find(".product-card img");
        Assert.Equal("https://example.com/gps.jpg", image.GetAttribute("src"));
        Assert.Equal("Truck GPS", image.GetAttribute("alt"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
        Assert.Equal(2, component.FindAll(".no-image").Count);
    }

    [Theory]
    [InlineData(" gps ", "gps")]
    [InlineData("tools & parts/+?=雪", "tools & parts/+?=雪")]
    [InlineData("   ", "truck")]
    public void SearchRequestsBackendWithEncodedQuery(string query, string expected)
    {
        var component = RenderCatalog("""{"itemSummaries":[{"title":"Initial"}]}""");
        handler.Body = """{"itemSummaries":[{"title":"Live result"}]}""";
        component.Find("input").Change(query);
        component.Find("form").Submit();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("http://backend/api/products?q=" + Uri.EscapeDataString(expected), handler.Url);
            Assert.Equal("Live result", component.Find(".product-card h2").TextContent);
            Assert.False(component.Find("button").HasAttribute("disabled"));
        });
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"itemSummaries\":null}")]
    [InlineData("{\"itemSummaries\":[]}")]
    public void EmptyResultsShowEmptyState(string json)
    {
        var component = RenderCatalog(json);
        Assert.Contains("No products found", component.Markup);
        Assert.Empty(component.FindAll(".product-grid"));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void InvalidJsonShowsErrorAndAllowsRetry()
    {
        var component = RenderCatalog("invalid JSON");
        Assert.Equal("The product service returned an invalid response. Please try again.", component.Find("[role=alert]").TextContent);
        handler.Body = """{"itemSummaries":[{"title":"Recovered"}]}""";
        component.Find("form").Submit();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("Recovered", component.Find(".product-card h2").TextContent);
            Assert.Empty(component.FindAll("[role=alert]"));
        });
    }

    [Fact]
    public void BackendFailureShowsError()
    {
        handler.Status = HttpStatusCode.BadGateway;
        var component = RenderCatalog("{}");
        Assert.Contains("Could not load eBay products", component.Find("[role=alert]").TextContent);
        Assert.False(component.Find("button").HasAttribute("disabled"));
        Assert.Empty(component.FindAll(".product-grid"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
