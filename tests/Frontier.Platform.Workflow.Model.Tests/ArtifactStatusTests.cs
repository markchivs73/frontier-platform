namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class ArtifactStatusTests
{
    [Fact]
    public void List_Always_ReturnsAllFiveValuesInDeclarationOrder()
    {
        Assert.Equal(
            [
                ArtifactStatus.Empty,
                ArtifactStatus.Draft,
                ArtifactStatus.Approved,
                ArtifactStatus.Regenerating,
                ArtifactStatus.Waiting,
            ],
            ArtifactStatus.List);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("draft")]
    [InlineData("approved")]
    [InlineData("regenerating")]
    [InlineData("waiting")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ArtifactStatus.FromName(name).Name);
    }

    [Theory]
    [InlineData("empty", "draft", true)]
    [InlineData("empty", "approved", false)]
    [InlineData("draft", "approved", true)]
    [InlineData("draft", "draft", true)]
    [InlineData("approved", "regenerating", true)]
    [InlineData("approved", "draft", false)]
    [InlineData("regenerating", "draft", true)]
    [InlineData("regenerating", "waiting", true)]
    [InlineData("regenerating", "approved", false)]
    [InlineData("waiting", "regenerating", true)]
    [InlineData("waiting", "draft", false)]
    public void CanTransitionTo_GivenPair_ReturnsExpected(string from, string to, bool expected)
    {
        var result = ArtifactStatus.FromName(from).CanTransitionTo(ArtifactStatus.FromName(to));

        Assert.Equal(expected, result);
    }
}
