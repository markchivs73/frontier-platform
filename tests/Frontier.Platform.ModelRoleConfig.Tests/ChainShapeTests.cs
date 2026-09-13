using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA27 tests for <see cref="ChainShape"/>, the platform's half of the agent-entry guard.</summary>
public sealed class ChainShapeTests
{
    private static readonly AgentEntry EchoAgent = new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/echo",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    private static ModelEntry Model => (ModelEntry)Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];

    [Fact]
    public void EnsureValid_AllModelChain_ReturnsTheMapping()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        Assert.Same(mapping, ChainShape.EnsureValid(mapping));
    }

    [Fact]
    public void EnsureValid_AllAgentChain_ReturnsTheMapping()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1 with { Chain = [EchoAgent, EchoAgent with { ResourceName = "com.azure.foundry/echo-backup" }] };

        Assert.Same(mapping, ChainShape.EnsureValid(mapping));
    }

    [Fact]
    public void EnsureValid_MixedChain_IsAContractViolation()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1 with { Chain = [Model, EchoAgent] };

        var exception = Assert.Throws<ContractViolationException>(() => ChainShape.EnsureValid(mapping));

        Assert.Contains("never mixed", Assert.Single(exception.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void Violations_AgentWithWrongProvider_IsReported()
    {
        var violations = ChainShape.Violations([EchoAgent with { Provider = "anthropic" }]);

        Assert.Contains("must have provider 'a2a'", Assert.Single(violations), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "1.0")]
    [InlineData("com.azure.foundry/echo", " ")]
    public void Violations_AgentWithoutResourceNameOrVersion_IsReported(string name, string version)
    {
        var violations = ChainShape.Violations([EchoAgent with { ResourceName = name, ResourceVersion = version }]);

        Assert.Contains("agent entry must name its registry resource and version.", violations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Violations_AgentWithNonPositiveCost_IsReported(double cost)
    {
        // Decision 1A: a zero cost is admitted by every budget, which would make enforcement blind.
        var violations = ChainShape.Violations([EchoAgent with { CostPerInvocation = (decimal)cost }]);

        Assert.Contains("positive cost_per_invocation", Assert.Single(violations), StringComparison.Ordinal);
    }
}
