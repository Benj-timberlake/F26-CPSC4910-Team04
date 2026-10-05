using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages.Products;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductsPageTests : TestContext
{
    [Theory]
    [InlineData("driver", "Product Catalog", true)]
    [InlineData("sponsor", "Product Catalog", true)]
    public void ProductsPageDisplaysContentForAccountRole(string role, string heading, bool hasCatalog)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("test-user");
        auth.SetRoles(role);
        Services.AddHttpClient("Backend");

        var page = RenderComponent<ProductsPage>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Equal(hasCatalog, page.FindAll(".search-bar").Count > 0);
        Assert.Equal(role == "driver", page.HasComponent<DriverProducts>());
    }
}
