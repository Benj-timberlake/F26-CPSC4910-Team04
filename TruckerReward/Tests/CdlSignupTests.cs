using System.Net;
using FrontEnd;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CdlSignupTests
{
    [Theory]
    [InlineData(null, "/dashboard")]
    [InlineData("%PDF-1.7 test", "/dashboard?cdlUpload=failed")]
    [InlineData("not a PDF", "/register?error=")]
    public async Task SignupHandlesOptionalInvalidAndFailedUploads(string? content, string location)
    {
        var backend = new JsonBackend();
        backend.Routes["POST /auth/register"] = (HttpStatusCode.OK,
            """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton(TimeProvider.System);
        // Deliberately leave S3 unconfigured to exercise the retry path without AWS.
        builder.Services.AddSingleton(new CdlStorage(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        builder.Services.AddHttpClient("Backend", client => client.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.MapAccountEndpoints();
        await app.StartAsync();
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in new[] { ("username", "bob"), ("firstName", "Bob"), ("lastName", "Driver"),
                     ("email", "bob@example.com"), ("password", "Hunter22x!"), ("userType", "driver") })
            form.Add(new StringContent(value), name);
        if (content is not null)
            form.Add(new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(content)), "cdlLicense", "license.pdf");
        using var response = await app.GetTestClient().PostAsync("/account/register", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(location, response.Headers.Location!.OriginalString);
        if (location.StartsWith("/dashboard")) Assert.True(response.Headers.Contains("Set-Cookie"));
        else Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
