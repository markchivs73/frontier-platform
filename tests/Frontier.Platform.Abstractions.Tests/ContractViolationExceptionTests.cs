using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class ContractViolationExceptionTests
{
    [Fact]
    public void Constructor_ContractTypeAndViolations_SetsPropertiesAndMessage()
    {
        var exception = new ContractViolationException("ScopeSection", ["title is required", "objectives must not be empty"]);

        Assert.Equal("ScopeSection", exception.ContractType);
        Assert.Equal(["title is required", "objectives must not be empty"], exception.Violations);
        Assert.Equal("ScopeSection: title is required; objectives must not be empty", exception.Message);
    }

    [Fact]
    public void Constructor_Parameterless_SetsEmptyContractTypeAndViolations()
    {
        var exception = new ContractViolationException();

        Assert.Equal(string.Empty, exception.ContractType);
        Assert.Empty(exception.Violations);
    }

    [Fact]
    public void Constructor_Message_SetsMessageAndEmptyContractTypeAndViolations()
    {
        var exception = new ContractViolationException("boom");

        Assert.Equal("boom", exception.Message);
        Assert.Equal(string.Empty, exception.ContractType);
        Assert.Empty(exception.Violations);
    }

    [Fact]
    public void Constructor_MessageAndInnerException_SetsAllProperties()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new ContractViolationException("boom", inner);

        Assert.Equal("boom", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(string.Empty, exception.ContractType);
        Assert.Empty(exception.Violations);
    }
}
