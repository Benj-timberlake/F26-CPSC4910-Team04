using System.Net;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Auth;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PasswordManagerTests : TestContext
{
    private readonly JsonBackend backend = new();

    private void UseBackend() =>
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

    [Fact]
    public void ChangePasswordTellsTheManagerWhichAccount()
    {
        this.AddTestAuthorization().SetAuthorized("bob");
        UseBackend();

        var page = RenderComponent<ChangePassword>();

        var username = page.Find("input[name=username]");
        Assert.Equal("bob", username.GetAttribute("value"));
        Assert.Equal("username", username.GetAttribute("autocomplete"));
        Assert.True(username.HasAttribute("hidden"));
    }

    [Fact]
    public void NewPasswordsCarryTheRulesForGeneratedPasswords()
    {
        UseBackend();

        var register = RenderComponent<Register>();
        var reset = RenderComponent<FrontEnd.Shared.NewPasswordFields>();

        Assert.Contains("required: special", register.Find("#password").GetAttribute("passwordrules"));
        Assert.Contains("minlength: 8", reset.Find("#password").GetAttribute("passwordrules"));
    }

    [Fact]
    public async Task WellKnownChangePasswordGoesToThePasswordPage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddHttpClient();
        await using var app = builder.Build();
        app.MapAccountEndpoints();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/.well-known/change-password");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/password", response.Headers.Location!.ToString());
    }
}
