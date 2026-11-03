using System.Net;
using System.Security.Claims;
using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DevicesPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Sessions = """
        [{"id":2,"createdAt":"2026-11-02T09:00:00Z","lastSeenAt":"2026-11-02T10:00:00Z","ipAddress":"10.0.0.2","device":"Safari on iPhone","current":false},
         {"id":1,"createdAt":"2026-11-02T08:00:00Z","lastSeenAt":"2026-11-02T09:30:00Z","ipAddress":"10.0.0.1","device":"Chrome on macOS","current":true}]
        """;

    private IRenderedComponent<Devices> Render()
    {
        this.SignInAs("bob", null, "8", new Claim(FrontEnd.Auth.TruckerSignIn.SessionKeyClaim, "k1"));
        this.UseBackend(backend);
        return RenderComponent<Devices>();
    }

    [Fact]
    public void ListsDevicesAndOnlyOffersToSignOutTheOthers()
    {
        backend.Routes["GET /users/8/sessions?current=k1"] = (HttpStatusCode.OK, Sessions);
        backend.Routes["DELETE /users/8/sessions/2"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        Assert.Contains("This device", page.FindAll("tbody tr").Last().TextContent);
        var end = Assert.Single(page.FindAll("button.end"));
        end.Click();

        page.WaitForAssertion(() => Assert.Equal("Safari on iPhone will be signed out within five minutes.", page.Find(".tr-alert-success").TextContent));
        Assert.Contains("DELETE /users/8/sessions/2", backend.Requests);
    }
}
