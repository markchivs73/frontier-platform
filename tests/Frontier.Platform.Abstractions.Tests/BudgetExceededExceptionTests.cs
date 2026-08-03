using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class BudgetExceededExceptionTests
{
    [Fact]
    public void Constructor_PolicyIdAndReason_SetsPropertiesAndMessage()
    {
        var exception = new BudgetExceededException("advisory-sow-default", "invocation token budget exhausted");

        Assert.Equal("advisory-sow-default", exception.PolicyId);
        Assert.Equal("invocation token budget exhausted", exception.Reason);
        Assert.Equal("advisory-sow-default: invocation token budget exhausted", exception.Message);
    }

    [Fact]
    public void Constructor_Parameterless_SetsEmptyPolicyIdAndReason()
    {
        var exception = new BudgetExceededException();

        Assert.Equal(string.Empty, exception.PolicyId);
        Assert.Equal(string.Empty, exception.Reason);
    }

    [Fact]
    public void Constructor_Message_SetsMessageAndEmptyPolicyIdAndReason()
    {
        var exception = new BudgetExceededException("boom");

        Assert.Equal("boom", exception.Message);
        Assert.Equal(string.Empty, exception.PolicyId);
        Assert.Equal(string.Empty, exception.Reason);
    }

    [Fact]
    public void Constructor_MessageAndInnerException_SetsAllProperties()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new BudgetExceededException("boom", inner);

        Assert.Equal("boom", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(string.Empty, exception.PolicyId);
        Assert.Equal(string.Empty, exception.Reason);
    }
}
