using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>S13.187: the kill switch's permanent denial (doc 07 §10, doc 10 §3).</summary>
public sealed class GuardrailsSuspendedExceptionTests
{
    [Fact]
    public void Constructor_Parameterless_CarriesTheReasonCode()
    {
        var exception = new GuardrailsSuspendedException();

        Assert.Equal(GuardrailsSuspendedException.DefaultMessage, exception.Message);
        Assert.StartsWith("guardrails_suspended:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        Assert.Equal("boom", new GuardrailsSuspendedException("boom").Message);
    }

    [Fact]
    public void Constructor_MessageAndInnerException_SetsBoth()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new GuardrailsSuspendedException("boom", inner);

        Assert.Equal("boom", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
