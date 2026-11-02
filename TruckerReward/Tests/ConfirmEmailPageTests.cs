using System.Net;
using Bunit;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ConfirmEmailPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<ConfirmEmail> Render(string url)
    {
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        return RenderComponent<ConfirmEmail>();
    }

    [Fact]
    public void NothingIsUsedUpUntilTheButtonIsPressed()
    {
        backend.Routes["POST /auth/confirm-email"] = (HttpStatusCode.OK, """{"message":"bob@example.com is confirmed."}""");
        var page = Render("confirm-email?token=abc");
        Assert.Empty(backend.Bodies);

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal("bob@example.com is confirmed.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"token":"abc"}""", backend.Bodies["POST /auth/confirm-email"]);
    }

    [Fact]
    public void MissingTokenSaysSo()
    {
        var page = Render("confirm-email");
        Assert.Contains("missing its token", page.Find("[role=alert]").TextContent);
        Assert.Empty(page.FindAll("button"));
    }
}
