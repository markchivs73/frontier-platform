namespace Frontier.Platform.Abstractions.Tests;

public sealed class SmartEnumTests
{
    [Fact]
    public void List_Always_ReturnsAllDeclaredValuesInDeclarationOrder()
    {
        Assert.Equal([ExampleStatus.Draft, ExampleStatus.InProgress, ExampleStatus.Approved], ExampleStatus.List);
    }

    [Fact]
    public void FromName_KnownName_ReturnsMatchingValue()
    {
        Assert.Same(ExampleStatus.InProgress, ExampleStatus.FromName("in_progress"));
    }

    [Fact]
    public void FromName_UnknownName_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExampleStatus.FromName("unknown"));
    }

    [Fact]
    public void TryFromName_UnknownName_ReturnsFalse()
    {
        Assert.False(ExampleStatus.TryFromName("unknown", out _));
    }

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        Assert.True(ExampleStatus.Draft.Equals((object)ExampleStatus.Draft));
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        Assert.False(ExampleStatus.Draft.Equals((object)ExampleStatus.InProgress));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        Assert.False(ExampleStatus.Draft.Equals((object?)null));
    }

    [Fact]
    public void GetHashCode_SameName_AreEqual()
    {
        Assert.Equal(ExampleStatus.Draft.GetHashCode(), ExampleStatus.FromName("draft").GetHashCode());
    }

    [Fact]
    public void ToString_Always_ReturnsName()
    {
        Assert.Equal("draft", ExampleStatus.Draft.ToString());
    }
}
