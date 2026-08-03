
namespace Frontier.Platform.Audit.Tests;

public sealed class ValidatorStatusTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([ValidatorStatus.Pass, ValidatorStatus.Fail, ValidatorStatus.Warn], ValidatorStatus.List);
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("fail")]
    [InlineData("warn")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ValidatorStatus.FromName(name).Name);
    }

    [Fact]
    public void FromName_UnknownName_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidatorStatus.FromName("unknown"));
    }
}
