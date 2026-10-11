using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NewTableMigrationTests
{
    [Theory]
    [InlineData(typeof(Impersonation), "005_impersonations.sql")]
    [InlineData(typeof(PasskeyCredential), "006_passkeys.sql")]
    public void TablesMatchTheirMigration(Type entity, string file)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
            .Options);
        var table = db.Model.FindEntityType(entity)!;
        var sql = File.ReadAllText(Path.Combine(Migration004Tests.RepoRoot(), "db", "migrations", file));

        Assert.Contains($"CREATE TABLE IF NOT EXISTS {table.GetTableName()} (", sql);
        foreach (var column in table.GetProperties().Select(p => p.GetColumnName()))
            Assert.Contains($"    {column} ", sql);
        foreach (var fk in table.GetForeignKeys())
            Assert.Contains($"CONSTRAINT {fk.GetConstraintName()} ", sql);
    }
}
