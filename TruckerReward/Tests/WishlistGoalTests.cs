using System.Net;
using Bunit;
using FrontEnd.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class WishlistGoalTests : TestContext
{
    private JsonBackend Setup(string ids = "[\"gps\",\"hat\"]", string price = "10.25")
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/wishlist"] = (HttpStatusCode.OK, ids);
        backend.Routes["GET /api/products/detail?itemId=gps"] = (HttpStatusCode.OK,
            $$$"""{"itemId":"gps","price":{"value":"{{{price}}}","currency":"USD"}}""");
        backend.Routes["GET /api/products/detail?itemId=hat"] = (HttpStatusCode.OK,
            """{"itemId":"hat","price":{"value":"2.50","currency":"USD"}}""");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return backend;
    }

    [Theory]
    [InlineData(500, "775 points away")]
    [InlineData(1275, "You have enough points")]
    [InlineData(2000, "You have enough points")]
    public void GoalComparesTheEntireWishlistToTheBalance(int balance, string message)
    {
        Setup();
        var card = RenderComponent<WishlistGoal>(p => p.Add(c => c.UserId, 7).Add(c => c.Points, balance));
        card.WaitForAssertion(() => Assert.Contains(message, card.Find(".wishlist-remaining").TextContent));
        Assert.Contains("Wishlist total: 1,275 points", card.Find("section").TextContent);
        Assert.Equal("wishlist", card.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void EmptyWishlistShowsGuidanceWithoutFetchingProducts()
    {
        var backend = Setup("[]");
        var card = RenderComponent<WishlistGoal>(p => p.Add(c => c.UserId, 7));
        card.WaitForAssertion(() => Assert.Contains("Save items to your wishlist", card.Markup));
        Assert.Single(backend.Requests);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("-1")]
    public void UnavailablePriceDoesNotPresentAnIncompleteGoal(string price)
    {
        Setup(price: price);
        var card = RenderComponent<WishlistGoal>(p => p.Add(c => c.UserId, 7));
        card.WaitForAssertion(() => Assert.Contains("price could not be loaded", card.Find("[role=alert]").TextContent));
        Assert.Empty(card.FindAll(".wishlist-remaining"));
    }

    [Fact]
    public void ServiceFailureShowsAnErrorInsteadOfAnAffordableGoal()
    {
        var backend = Setup();
        backend.Routes["GET /users/7/wishlist"] = (HttpStatusCode.InternalServerError, "");
        var card = RenderComponent<WishlistGoal>(p => p.Add(c => c.UserId, 7));
        card.WaitForAssertion(() => Assert.Contains("Couldn't load", card.Find("[role=alert]").TextContent));
        Assert.Empty(card.FindAll(".wishlist-remaining"));
    }

    [Fact]
    public void BalanceChangesUpdateTheGoalWithoutReloadingPrices()
    {
        var backend = Setup();
        var card = RenderComponent<WishlistGoal>(p => p.Add(c => c.UserId, 7).Add(c => c.Points, 500));
        card.WaitForAssertion(() => Assert.Contains("775 points away", card.Markup));
        var requests = backend.Requests.Count;
        card.SetParametersAndRender(p => p.Add(c => c.Points, 1000));
        Assert.Contains("275 points away", card.Markup);
        Assert.Equal(requests, backend.Requests.Count);
    }
}
