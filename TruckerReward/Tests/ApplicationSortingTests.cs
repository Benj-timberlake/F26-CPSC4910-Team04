using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ApplicationSortingTests : TestContext
{
    [Fact]
    public void SponsorCanSortEveryStatusByNameOrTimeApplied()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sponsor");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "4"));
        var backend = new JsonBackend();
        var rows = new[] { "active", "approved", "rejected" }.SelectMany((status, index) =>
            new[]
            {
                new { id = index * 10 + 1, applicantName = "Zoe", status, applicationTimestamp = "2026-11-01T12:00:00Z", responseTimestamp = "2026-11-04T12:00:00Z" },
                new { id = index * 10 + 2, applicantName = "alice", status, applicationTimestamp = "2026-11-03T12:00:00Z", responseTimestamp = "2026-11-03T12:00:00Z" },
                new { id = index * 10 + 3, applicantName = "Bob", status, applicationTimestamp = "2026-11-02T12:00:00Z", responseTimestamp = "2026-11-05T12:00:00Z" }
            });
        backend.Routes["GET /users/4/applications/driver"] = (HttpStatusCode.OK, JsonSerializer.Serialize(rows));
        Services.AddHttpClient("Backend", client => client.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var page = RenderComponent<Applications>();
        page.WaitForAssertion(() => Assert.Equal(9, page.FindAll(".application-card" ).Count));

        AssertOrder("Zoe", "Bob", "alice");
        page.Find("#application-sort").Change("NameAscending");
        AssertOrder("alice", "Bob", "Zoe");
        page.Find("#application-sort").Change("NameDescending");
        AssertOrder("Zoe", "Bob", "alice");
        page.Find("#application-sort").Change("NewestFirst");
        AssertOrder("alice", "Bob", "Zoe");
        page.Find("#application-sort").Change("OldestFirst");
        AssertOrder("Zoe", "Bob", "alice");

        void AssertOrder(params string[] expected)
        {
            foreach (var panel in page.FindAll(".application-panel"))
                Assert.Equal(expected, panel.QuerySelectorAll("h3").Select(heading => heading.TextContent));
        }
    }
}
