using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ChangeUsernamePageTests : TestContext
{
    [Fact]
    public void FormStartsWithTheCurrentUsernameAndPostsToTheAccountEndpoint()
    {
        this.AddTestAuthorization().SetAuthorized("bob");

        var page = RenderComponent<ChangeUsername>();

        Assert.Equal("bob", page.Find("#username").GetAttribute("value"));
        Assert.Equal("account/change-username", page.Find("form").GetAttribute("action"));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void ErrorFromTheBackendIsShown()
    {
        this.AddTestAuthorization().SetAuthorized("bob");
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("account/username?error=" + Uri.EscapeDataString("That username is already taken."));

        var page = RenderComponent<ChangeUsername>();

        Assert.Equal("That username is already taken.", page.Find("[role=alert]").TextContent);
    }
}
