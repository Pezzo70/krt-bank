using Domain.ValueObjects;

namespace Domain.Tests.ValueObjects;

public class AccountNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Create_ShouldRejectBlankNamesWithoutThrowing(string? name)
    {
        var result = AccountName.Create(name);

        Assert.True(result.IsFailed);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Create_ShouldTrimEdgesAndPreserveInternalSpaces()
    {
        var result = AccountName.Create("  Example  account  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Example  account", result.Value.Value);
    }

    [Fact]
    public void Equality_ShouldMatchNormalizedNames()
    {
        var first = AccountName.Create("Example account").Value;
        var second = AccountName.Create("  Example account  ").Value;

        Assert.Equal(first, second);
        Assert.NotEqual(first, AccountName.Create("Other account").Value);
    }
}
