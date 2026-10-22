using System.Net;
using FrontEnd;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CloudFrontRedirectTests : IAsyncDisposable
{
    private WebApplication? app;

    private async Task<HttpClient> Start(string? publicUrl)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        app = builder.Build();
        app.UseCloudFrontRedirect(publicUrl);
        app.MapGet("/{**path}", () => "page");
        app.MapPost("/{**path}", () => "posted");
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage ViaCloudFront(HttpMethod method, string path, string host = "team04.example.com")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        request.Headers.Add("CloudFront-Forwarded-Proto", "https");
        return request;
    }

    [Fact]
    public async Task DirectRequestIsSentToThePublicHttpsAddress()
    {
        var client = await Start("https://team04.example.com");
        var response = await client.GetAsync("/products/5?tab=info");
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("https://team04.example.com/products/5?tab=info", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TrailingSlashOnThePublicUrlIsIgnored()
    {
        var client = await Start("https://team04.example.com/");
        var response = await client.GetAsync("/login");
        Assert.Equal("https://team04.example.com/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task RequestThroughCloudFrontIsServed()
    {
        var client = await Start("https://team04.example.com");
        var response = await client.SendAsync(ViaCloudFront(HttpMethod.Post, "/login"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("posted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OtherHostThroughCloudFrontIsSentToThePublicAddress()
    {
        var client = await Start("https://team04.example.com");
        var response = await client.SendAsync(ViaCloudFront(HttpMethod.Get, "/login?x=1", "d123.cloudfront.net"));
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("https://team04.example.com/login?x=1", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://BEANSTALK-04-env.eba-w5yp5iyf.us-east-1.elasticbeanstalk.com")]
    public async Task NoHttpsPublicUrlMeansNoRedirect(string? publicUrl)
    {
        var client = await Start(publicUrl);
        var response = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }
}
