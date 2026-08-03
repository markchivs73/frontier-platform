using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class ContextRequestTests
{
    [Fact]
    public void Validate_WellFormedRequest_DoesNotThrow()
    {
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["company-profile"],
            DynamicFields = ["latest-notes"],
        };

        request.Validate();
    }

    [Fact]
    public void Validate_EmptyBaselineComponents_Throws()
    {
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = [],
            DynamicFields = [],
        };

        var exception = Assert.Throws<ContractViolationException>(request.Validate);

        Assert.Contains("baseline_components must not be empty.", exception.Violations);
    }

    [Fact]
    public void Validate_WildcardBaselineComponent_Throws()
    {
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["*"],
            DynamicFields = [],
        };

        var exception = Assert.Throws<ContractViolationException>(request.Validate);

        Assert.Contains("baseline_components must be component-scoped; '*' is not allowed.", exception.Violations);
    }

    [Fact]
    public void Validate_RequiresRealTimeWithSources_DoesNotThrow()
    {
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["company-profile"],
            DynamicFields = [],
            RequiresRealTime = true,
            RealTimeSources = ["mcp-autotask"],
        };

        request.Validate();
    }

    [Fact]
    public void Validate_RealTimeSourcesWithoutRequiresRealTime_Throws()
    {
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["company-profile"],
            DynamicFields = [],
            RequiresRealTime = false,
            RealTimeSources = ["mcp-autotask"],
        };

        var exception = Assert.Throws<ContractViolationException>(request.Validate);

        Assert.Contains("real_time_sources must be empty when requires_real_time is false.", exception.Violations);
    }
}
