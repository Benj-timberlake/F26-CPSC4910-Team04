using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class Migration014Tests
{
    [Fact]
    public void AccountCompanyMembershipReferencesUsers()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
            .Options);
        var membership = db.Model.FindEntityType(typeof(AccountsCompany))!;
        var accountForeignKey = Assert.Single(membership.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Single().Name == nameof(AccountsCompany.AccountId));

        Assert.Equal(typeof(User), accountForeignKey.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void MigrationReplacesTheIncorrectHistoryForeignKey()
    {
        var sql = File.ReadAllText(Path.Combine(Migration004Tests.RepoRoot(), "db", "migrations", "014_fix_accounts_companies_account_fk.sql"));

        Assert.Contains("DROP FOREIGN KEY accounts_companies_ibfk_1", sql);
        Assert.Contains("FOREIGN KEY (account_id) REFERENCES users (id)", sql);
        Assert.DoesNotContain("REFERENCES accounts_history", sql);
    }
}
