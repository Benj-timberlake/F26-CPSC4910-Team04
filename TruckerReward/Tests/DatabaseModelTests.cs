using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DatabaseModelTests
{
    [Fact]
    public void MySqlModelMapsCartTableToOneEntity()
    {
        // Building the production provider's model requires no database connection.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
            .Options;
        using var db = new AppDbContext(options);

        var cart = Assert.Single(db.Model.GetEntityTypes(), entity => entity.GetTableName() == "cart");
        Assert.Equal(typeof(CartItem), cart.ClrType);
        Assert.Equal(2, cart.GetForeignKeys().Count());
        Assert.All(cart.GetProperties(), property => Assert.False(property.IsShadowProperty()));
    }
}
