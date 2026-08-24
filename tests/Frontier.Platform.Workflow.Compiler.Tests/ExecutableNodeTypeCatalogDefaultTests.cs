using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.25: the compiler's executable-node-type catalogue used to default to permissive —
/// everything validates. That is fail-**open**: with no override, the compiler publishes
/// workflows the runtime rejects at execution, and nothing goes red. The hole was previously
/// closed only by a consumer remembering to register an override.
/// </summary>
public sealed class ExecutableNodeTypeCatalogDefaultTests
{
    [Fact]
    public void Default_IsTheInterpretersActualCapabilities_NotPermissive()
    {
        var services = new ServiceCollection().AddFrontierWorkflowCompiler();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IExecutableNodeTypeCatalog));

        Assert.Equal(typeof(OrchestratorExecutableNodeTypeCatalog), descriptor.ImplementationType);
    }

    [Fact]
    public void Default_RejectsANodeTypeTheInterpreterCannotRun()
    {
        var catalogue = new OrchestratorExecutableNodeTypeCatalog();

        var unsupported = NodeType.List.Where(t => !OrchestratorCapabilities.Supports(t)).ToList();

        // If this ever becomes empty the assertion below is vacuous, so say so rather than pass.
        Assert.NotEmpty(unsupported);
        Assert.All(unsupported, t => Assert.False(catalogue.IsExecutable(t)));
    }

    [Fact]
    public void Default_AcceptsEveryNodeTypeTheInterpreterRuns() =>
        Assert.All(OrchestratorCapabilities.SupportedNodeTypes,
            t => Assert.True(new OrchestratorExecutableNodeTypeCatalog().IsExecutable(t)));

    [Fact]
    public void Default_NamesAgreeWithWhatItAccepts()
    {
        // The names feed the design agent's schema; a catalogue that accepts one set and
        // advertises another would make the agent design workflows it is then told are invalid.
        var catalogue = new OrchestratorExecutableNodeTypeCatalog();

        Assert.Equal(
            OrchestratorCapabilities.SupportedNodeTypes.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal),
            catalogue.ExecutableNodeTypeNames);
    }

    [Fact]
    public void Permissive_IsStillAvailableForDeliberateUse() =>
        Assert.All(NodeType.List, t => Assert.True(new PermissiveExecutableNodeTypeCatalog().IsExecutable(t)));
}
