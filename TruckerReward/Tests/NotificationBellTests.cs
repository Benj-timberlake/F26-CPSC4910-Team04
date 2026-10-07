using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NotificationBellTests : TestContext
{
    private readonly CountBackend backend = new();

    private IRenderedComponent<NotificationBell> Render(int unread)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        backend.Count = unread;
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<NotificationBell>();
    }

    [Fact]
    public void ShowsTheUnreadCountAndLinksToTheNotificationsPage()
    {
        var bell = Render(3);

        bell.WaitForAssertion(() => Assert.Equal("3", bell.Find(".notification-count").TextContent));
        Assert.Equal("notifications", bell.Find("a").GetAttribute("href"));
        Assert.Equal("Notifications, 3 unread", bell.Find("a").GetAttribute("aria-label"));
        Assert.Equal(["/users/7/notifications/unread"], backend.Requests);
    }

    [Fact]
    public void NoBadgeWhenNothingIsUnread()
    {
        var bell = Render(0);
        bell.WaitForAssertion(() => Assert.Single(backend.Requests));
        Assert.Empty(bell.FindAll(".notification-count"));
    }

    [Fact]
    public void LargeCountsAreCapped()
    {
        var bell = Render(250);
        bell.WaitForAssertion(() => Assert.Equal("99+", bell.Find(".notification-count").TextContent));
    }

    [Fact]
    public void RefreshesTheCountOnNavigation()
    {
        var bell = Render(1);
        bell.WaitForAssertion(() => Assert.Equal("1", bell.Find(".notification-count").TextContent));

        backend.Count = 4;
        Services.GetRequiredService<NavigationManager>().NavigateTo("products");

        bell.WaitForAssertion(() => Assert.Equal("4", bell.Find(".notification-count").TextContent));
        Assert.Equal(2, backend.Requests.Count);
    }

    [Fact]
    public void BadgeIsHiddenOnTheNotificationsPage()
    {
        var bell = Render(2);
        bell.WaitForAssertion(() => Assert.Single(bell.FindAll(".notification-count")));

        Services.GetRequiredService<NavigationManager>().NavigateTo("notifications");

        bell.WaitForAssertion(() => Assert.Empty(bell.FindAll(".notification-count")));
    }

    private sealed class CountBackend : HttpMessageHandler
    {
        public int Count { get; set; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"count":{{Count}}}""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
