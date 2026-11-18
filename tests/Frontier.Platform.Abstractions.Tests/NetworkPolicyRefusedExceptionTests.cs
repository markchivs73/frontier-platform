using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>Consumer S13.300: a connect-time network policy's permanent refusal (ADR-PA48, amended 2026-11-17).</summary>
public sealed class NetworkPolicyRefusedExceptionTests
{
    [Fact]
    public void Constructor_Parameterless_HasNoInnerException()
    {
        var exception = new NetworkPolicyRefusedException();

        Assert.Null(exception.InnerException);
        Assert.False(string.IsNullOrEmpty(exception.Message));
    }

    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        Assert.Equal("10.0.0.1 is private", new NetworkPolicyRefusedException("10.0.0.1 is private").Message);
    }

    [Fact]
    public void Constructor_MessageAndInnerException_SetsBoth()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new NetworkPolicyRefusedException("refused", inner);

        Assert.Equal("refused", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
