using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>S6.5 coverage: value-type semantics and implicit conversions on <see cref="EngagementId"/>.</summary>
public sealed class EngagementIdTests
{
    [Fact]
    public void ToString_ReturnsValue()
    {
        var id = new EngagementId("eng-42");

        Assert.Equal("eng-42", id.ToString());
    }

    [Fact]
    public void ImplicitFromString_CreatesEngagementId()
    {
        EngagementId id = "eng-99";

        Assert.Equal("eng-99", id.Value);
    }

    [Fact]
    public void ImplicitToString_ReturnsValue()
    {
        var id = new EngagementId("eng-77");

        string value = id;

        Assert.Equal("eng-77", value);
    }

    [Fact]
    public void ValueEquality_SameValue_Equal()
    {
        var a = new EngagementId("eng-1");
        var b = new EngagementId("eng-1");

        Assert.Equal(a, b);
    }

    [Fact]
    public void ValueEquality_DifferentValue_NotEqual()
    {
        var a = new EngagementId("eng-1");
        var b = new EngagementId("eng-2");

        Assert.NotEqual(a, b);
    }
}
