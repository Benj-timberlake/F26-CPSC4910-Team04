using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductDetailTests : TestContext
{
    private readonly Handler handler = new();

    public ProductDetailTests()
    {
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/product?itemId=v1%7C123%7C0");
    }

    [Fact]
    public void DetailsRenderAndBuyHasNoAction()
    {
        handler.Body = """
            {"itemId":"v1|123|0","title":"GPS","price":{"value":"29.50","currency":"USD"},
             "image":{"imageUrl":"https://example.com/photo.jpg"},"description":"<h2>Details</h2><script>alert(1)</script>"}
            """;
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Equal("GPS", component.Find("h1").TextContent));
        Assert.Equal("http://backend/api/products/detail?itemId=v1%7C123%7C0", handler.Url);
        Assert.Equal("29.50 USD", component.Find(".detail-price").TextContent);
        Assert.Equal("https://example.com/photo.jpg", component.Find(".detail-photo img").GetAttribute("src"));
        Assert.True(component.Find(".buy-button").HasAttribute("disabled"));
        Assert.Empty(component.FindAll("form"));
        var frame = component.Find("iframe");
        Assert.Equal("", frame.GetAttribute("sandbox"));
        Assert.Contains("default-src 'none'", frame.GetAttribute("srcdoc"));
        Assert.Empty(component.FindAll("script"));
    }

    [Theory]
    [InlineData(404, "This product is no longer available.")]
    [InlineData(502, "Could not load the product.")]
    public void ErrorsShowWithoutBuyButton(int status, string message)
    {
        handler.Status = (HttpStatusCode)status;
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Contains(message, component.Find("[role=alert]").TextContent));
        Assert.Empty(component.FindAll(".buy-button"));
    }

    [Fact]
    public void MissingOptionalDetailsHaveFallbacks()
    {
        handler.Body = """{"itemId":"v1|123|0","title":"GPS"}""";
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Contains("No description available.", component.Markup));
        Assert.Contains("No image available", component.Markup);
        Assert.Contains("Price unavailable", component.Markup);
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    public void InvalidResponseCanBeRetried(string body)
    {
        handler.Body = body;
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Contains("invalid response", component.Find("[role=alert]").TextContent));
        Assert.Empty(component.FindAll(".buy-button"));
        handler.Body = """{"itemId":"v1|123|0","title":"Recovered"}""";
        component.Find("button").Click();
        component.WaitForAssertion(() => Assert.Equal("Recovered", component.Find("h1").TextContent));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void MissingItemIdDoesNotRequestBackend()
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/product");
        var component = RenderComponent<ProductDetail>();
        Assert.Contains("No product selected", component.Find("[role=alert]").TextContent);
        Assert.Null(handler.Url);
    }

    [Fact]
    public void ShortDescriptionIsEscapedAndBackLinkReturnsToCatalog()
    {
        handler.Body = """{"itemId":"v1|123|0","shortDescription":"<script>alert(1)</script>"}""";
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Contains("<script>alert(1)</script>", component.Find(".description p").TextContent));
        Assert.Empty(component.FindAll("script, iframe"));
        Assert.Equal("/products", component.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task NavigationLoadsNewProductInsteadOfKeepingOldDetails()
    {
        handler.Body = """{"itemId":"v1|123|0","title":"First"}""";
        var component = RenderComponent<ProductDetail>();
        component.WaitForAssertion(() => Assert.Equal("First", component.Find("h1").TextContent));
        handler.Body = """{"itemId":"v1|456|0","title":"Second"}""";
        await component.InvokeAsync(() => Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/product?itemId=v1%7C456%7C0"));
        component.WaitForAssertion(() => Assert.Equal("Second", component.Find("h1").TextContent));
        Assert.EndsWith("itemId=v1%7C456%7C0", handler.Url);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
