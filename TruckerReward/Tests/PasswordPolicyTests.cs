using Xunit;

namespace TruckerReward.Tests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Trucks4Life!")]
    [InlineData("aB3#aB3#")]
    [InlineData("correct Horse 9?")]
    public void AcceptsPasswordsThatMeetEveryRule(string password) =>
        Assert.Null(PasswordPolicy.Check(password));

    [Theory]
    [InlineData("", "at least 8")]
    [InlineData("Ab1", "at least 8")]
    [InlineData("trucks4life", "upper")]
    [InlineData("TRUCKS4LIFE", "lower")]
    [InlineData("TrucksForLife", "number")]
    [InlineData("Trucks4Life", "special")]
    [InlineData("Trucks 4 Life", "special")]
    public void RejectsWithTheRuleThatFailed(string password, string expectedInMessage)
    {
        var message = PasswordPolicy.Check(password);
        Assert.NotNull(message);
        Assert.Contains(expectedInMessage, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescriptionMatchesTheRules() =>
        Assert.Equal("At least 8 characters with an upper case letter, a lower case letter, a number and a special character (like ! or #).", PasswordPolicy.Description);
}
