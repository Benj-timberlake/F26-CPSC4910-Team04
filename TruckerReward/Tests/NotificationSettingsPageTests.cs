using System.Net;
using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NotificationSettingsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Settings = """
        [{"category":"points","label":"Points added or deducted","enabled":true,"emailed":false},
         {"category":"applications","label":"Application updates","enabled":true,"emailed":true}]
        """;

    private IRenderedComponent<NotificationSettings> Render()
    {
        this.SignInAs("bob", null, "7");
        this.UseBackend(backend);
        return RenderComponent<NotificationSettings>();
    }

    [Fact]
    public void SavesWhatWasTicked()
    {
        backend.Routes["GET /users/7/notification-settings"] = (HttpStatusCode.OK, Settings);
        backend.Routes["PUT /users/7/notification-settings"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        page.Find("input[aria-label='Email Points added or deducted']").Change(true);
        page.Find("input[aria-label='Application updates on']").Change(false);
        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal("Saved.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""[{"category":"points","enabled":true,"emailed":true},{"category":"applications","enabled":false,"emailed":true}]""",
            backend.Bodies["PUT /users/7/notification-settings"]);
    }

    [Fact]
    public void EmailBoxIsDisabledWhileACategoryIsOff()
    {
        backend.Routes["GET /users/7/notification-settings"] = (HttpStatusCode.OK, Settings);
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("input[aria-label='Points added or deducted on']").Change(false);

        Assert.True(page.Find("input[aria-label='Email Points added or deducted']").HasAttribute("disabled"));
    }

    [Fact]
    public void FailedSaveIsShown()
    {
        backend.Routes["GET /users/7/notification-settings"] = (HttpStatusCode.OK, Settings);
        backend.Routes["PUT /users/7/notification-settings"] = (HttpStatusCode.BadRequest, """{"message":"x"}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal("Couldn't save your settings.", page.Find(".tr-alert-error").TextContent));
    }
}
