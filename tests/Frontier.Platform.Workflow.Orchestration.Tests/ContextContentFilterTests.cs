using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="ContextContentFilter"/>.</summary>
public sealed class ContextContentFilterTests
{
    [Fact]
    public void Filter_EmptyKeys_ReturnsEmptyObject()
    {
        var result = ContextContentFilter.Filter("""{"a":1,"b":2}""", [], "baseline_components");

        Assert.Equal("{}", result);
    }

    [Fact]
    public void Filter_PresentKeys_ReturnsFilteredJsonInRequestedOrder()
    {
        var result = ContextContentFilter.Filter("""{"a":1,"b":2,"c":3}""", ["c", "a"], "baseline_components");

        Assert.Equal("""{"c":3,"a":1}""", result);
    }

    [Fact]
    public void Filter_MissingKey_ThrowsContractViolationException()
    {
        var exception = Assert.Throws<ContractViolationException>(() =>
            ContextContentFilter.Filter("""{"a":1}""", ["a", "missing"], "dynamic_fields"));

        Assert.Equal("dynamic_fields", exception.ContractType);
        Assert.Contains("missing field 'missing'", exception.Violations);
    }
}
