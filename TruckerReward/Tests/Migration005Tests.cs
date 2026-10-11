using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class Migration005Tests
{
    [Fact]
    public void ImpersonationsMatchTheMigration()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
            .Options);
        var views = db.Model.FindEntityType(typeof(Impersonation))!;
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "db", "migrations", "005_impersonations.sql"));

        Assert.Contains($"CREATE TABLE IF NOT EXISTS {views.GetTableName()} (", sql);
        foreach (var column in views.GetProperties().Select(p => p.GetColumnName()))
            Assert.Contains($"    {column} ", sql);
        foreach (var fk in views.GetForeignKeys())
            Assert.Contains($"CONSTRAINT {fk.GetConstraintName()} ", sql);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir!.FullName, "db")))
            dir = dir.Parent;
        return dir.FullName;
    }
}
