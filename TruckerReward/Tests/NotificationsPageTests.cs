using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using NotificationsPage = FrontEnd.Pages.Notifications;

namespace TruckerReward.Tests;

public sealed class NotificationsPageTests : TestContext
{
    private readonly FakeBackend backend = new();

    private IRenderedComponent<NotificationsPage> Render(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        backend.ListJson = json;
        backend.ListStatus = status;
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<NotificationsPage>();
    }

    [Fact]
    public void ListsNotificationsAndMarksThemReadOnce()
    {
        var page = Render("""
            [{"id":2,"subject":"Points added","body":"You earned 50 points.","timestamp":"2026-10-07T12:00:00","read":false},
             {"id":1,"subject":"Welcome","body":"Hi","timestamp":"2026-10-06T09:30:00","read":true}]
            """);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".notification-list li").Count));
        var items = page.FindAll(".notification-list li").ToList();
        Assert.Contains("unread", items[0].ClassList);
        Assert.DoesNotContain("unread", items[1].ClassList);
        Assert.Contains("You earned 50 points.", items[0].TextContent);
        Assert.Contains("2026-10-07 12:00 UTC", items[0].TextContent);

        page.WaitForAssertion(() => Assert.Equal(["POST /users/7/notifications/read"], backend.Posts));
        page.Render();
        Assert.Single(backend.Posts);
    }

    [Fact]
    public void AllReadSendsNothing()
    {
        var page = Render("""[{"id":1,"subject":"Welcome","body":"Hi","timestamp":"2026-10-06T09:30:00","read":true}]""");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".notification-list li")));
        Assert.Empty(backend.Posts);
    }

    [Fact]
    public void EmptyListSaysSo()
    {
        var page = Render("[]");
        page.WaitForAssertion(() => Assert.Contains("Nothing yet", page.Markup));
        Assert.Empty(backend.Posts);
    }

    [Fact]
    public void BackendFailureShowsAnError()
    {
        var page = Render("", HttpStatusCode.InternalServerError);
        page.WaitForAssertion(() => Assert.Contains("Couldn't reach the backend", page.Find("[role=alert]").TextContent));
    }

    private sealed class FakeBackend : HttpMessageHandler
    {
        public string ListJson { get; set; } = "[]";
        public HttpStatusCode ListStatus { get; set; } = HttpStatusCode.OK;
        public List<string> Posts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add($"POST {request.RequestUri!.AbsolutePath}");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(new HttpResponseMessage(ListStatus)
            {
                Content = new StringContent(ListJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
