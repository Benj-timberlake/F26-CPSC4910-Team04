using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class Migration004Tests
{
    private static AppDbContext MySqlModel() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
        .Options);

    [Fact]
    public void UsersMapTheStatusColumnAndAllowNullPasswords()
    {
        using var db = MySqlModel();
        var users = db.Model.FindEntityType(typeof(User))!;

        Assert.Equal("status", users.FindProperty(nameof(User.Status))!.GetColumnName());
        Assert.Equal("enum('active','inactive')", users.FindProperty(nameof(User.Status))!.GetColumnType());
        Assert.True(users.FindProperty(nameof(User.Password))!.IsNullable);
        Assert.Equal(User.Active, new User().Status);
    }

    [Fact]
    public void PreferencesMapToTheirOwnTable()
    {
        using var db = MySqlModel();
        var prefs = db.Model.FindEntityType(typeof(NotificationPreference))!;

        Assert.Equal("notification_preferences", prefs.GetTableName());
        Assert.Equal(["user_id", "category"], prefs.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName()));
        Assert.Equal("fk_notification_preferences_user", Assert.Single(prefs.GetForeignKeys()).GetConstraintName());
    }

    [Fact]
    public void MigrationDropsTheDoubleCountingTrigger()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "db", "migrations", "004_status_sso_preferences_points.sql"));

        Assert.Contains("DROP TRIGGER IF EXISTS points_history_after_insert;", sql);
        Assert.Contains("ALTER TABLE accounts_history MODIFY password VARCHAR(255) NULL;", sql);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir!.FullName, "db")))
            dir = dir.Parent;
        return dir.FullName;
    }
}
