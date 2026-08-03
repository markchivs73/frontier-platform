namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4 tests for the <see cref="FailureClass"/> smart enum (doc 10 §1, §3).</summary>
public sealed class FailureClassTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([FailureClass.Transient, FailureClass.Deferred, FailureClass.Permanent], FailureClass.List);
    }

    [Theory]
    [InlineData("transient")]
    [InlineData("deferred")]
    [InlineData("permanent")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, FailureClass.FromName(name).Name);
    }

    [Fact]
    public void Transient_IsRetryable()
    {
        Assert.True(FailureClass.Transient.IsRetryable);
    }

    [Fact]
    public void Deferred_IsRetryable()
    {
        Assert.True(FailureClass.Deferred.IsRetryable);
    }

    [Fact]
    public void Permanent_IsNotRetryable()
    {
        Assert.False(FailureClass.Permanent.IsRetryable);
    }
}
