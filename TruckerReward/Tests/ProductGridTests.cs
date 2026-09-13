using Bunit;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductGridTests : TestContext
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "product-grid-tests", Guid.NewGuid().ToString());

    public ProductGridTests()
    {
        Directory.CreateDirectory(root);
        Services.AddHttpClient();
        Services.AddSingleton<IWebHostEnvironment>(new TestEnvironment { WebRootPath = root });
    }

    private IRenderedComponent<Products> RenderCatalog(string json)
    {
        File.WriteAllText(Path.Combine(root, "sample-products.json"), json);
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
    [InlineData(" gps ", 1)]
    [InlineData("TRUCK", 2)]
    [InlineData("   ", 2)]
    [InlineData("unmatched", 0)]
    public void SearchFiltersTitlesIgnoringCaseAndSurroundingWhitespace(string query, int count)
    {
        var component = RenderCatalog("""
            {"itemSummaries":[{"title":"Truck GPS"},{"title":"Truck gloves"}]}
            """);
        component.Find("input").Change(query);
        component.Find("form").Submit();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[role=status]"));
            Assert.Equal(count, component.FindAll(".product-card").Count);
            Assert.False(component.Find("button").HasAttribute("disabled"));
            if (count == 0)
                Assert.Contains("No products found", component.Markup);
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
        Assert.Equal("The sample JSON is not formatted correctly.", component.Find("[role=alert]").TextContent);
        File.WriteAllText(Path.Combine(root, "sample-products.json"), """{"itemSummaries":[{"title":"Recovered"}]}""");
        component.Find("form").Submit();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("Recovered", component.Find(".product-card h2").TextContent);
            Assert.Empty(component.FindAll("[role=alert]"));
        });
    }

    [Fact]
    public void MissingFileShowsReadError()
    {
        var component = RenderComponent<Products>();
        component.WaitForAssertion(() => Assert.Contains("Could not read file:", component.Find("[role=alert]").TextContent));
        Assert.False(component.Find("button").HasAttribute("disabled"));
        Assert.Empty(component.FindAll(".product-grid"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Directory.Delete(root, recursive: true);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "FrontEnd";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
