using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Schema;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7h (ADR-DC7): the design surface may not advertise what the runtime cannot execute.
///
/// Both rules exist because a real designed workflow reached a live test run and failed there:
/// once with a <c>parallel</c> node the orchestrator refuses, and once with an internal projection
/// as an <c>output_contract_type</c> the model provider refuses. Both had passed validation with
/// zero findings.
/// </summary>
public sealed class S137hSurfaceRuleTests
{
    // ── structure.node-type-supported ──

    /// <summary>Stands in for a runtime that executes only the Phase-1 pair.</summary>
    private sealed class PhaseOneRuntime : IExecutableNodeTypeCatalog
    {
        public bool IsExecutable(NodeType nodeType) => nodeType == NodeType.AgentTask || nodeType == NodeType.HumanGate;
        public IReadOnlyList<string> ExecutableNodeTypeNames { get; } = ["agent_task", "human_gate"];
    }

    [Fact]
    public async Task NodeTypeSupported_ExecutableNodesOnly_NoFindings()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a")]);

        var findings = await new NodeTypeSupportedRule(new PhaseOneRuntime())
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task NodeTypeSupported_ParallelNode_IsRejectedWithAnActionableMessage()
    {
        // The exact shape that reached a live run and died: a parallel fan-out after a gate.
        var parallel = new ParallelNode
        {
            NodeId = "finalize_parallel",
            BranchNodeIds = ["update_ticket", "assign_booking"],
            JoinNodeId = "confirm_completion",
        };
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a"), parallel]);

        var finding = Assert.Single(await new NodeTypeSupportedRule(new PhaseOneRuntime())
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));

        Assert.Equal("structure.node-type-supported", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("finalize_parallel", finding.NodeId);
        Assert.Equal("node_type", finding.FieldPath);
        Assert.Contains("parallel", finding.Message, StringComparison.Ordinal);
        // The designer must be told what they *can* use, not merely what they can't.
        Assert.Contains("agent_task", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NodeTypeSupported_PermissiveRuntime_AcceptsEverything()
    {
        var parallel = new ParallelNode { NodeId = "p", BranchNodeIds = ["a"], JoinNodeId = "j" };
        var definition = S930Fixtures.Build([parallel]);

        var findings = await new NodeTypeSupportedRule(new PermissiveExecutableNodeTypeCatalog())
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public void NodeTypeSupported_NullCatalog_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new NodeTypeSupportedRule(null!));

    [Fact]
    public async Task NodeTypeSupported_NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new NodeTypeSupportedRule(new PhaseOneRuntime()).EvaluateAsync(null!, CancellationToken.None));

    // ── data.output-contract-bindable ──

    [Fact]
    public async Task OutputContractBindable_FlatStepContract_NoFindings()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a", outputContract: nameof(UpdateResult))]);

        var findings = await new OutputContractBindableRule(new ReflectionContractTypeCatalog(TestContractSet.Instance))
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task OutputContractBindable_ProjectionWithAnOpenMap_IsRejected()
    {
        // DictionaryShapedProjection carries Dictionary<string, EngagementArtifactProgress>.
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("confirm_completion", outputContract: nameof(DictionaryShapedProjection))]);

        var finding = Assert.Single(await new OutputContractBindableRule(new ReflectionContractTypeCatalog(TestContractSet.Instance))
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));

        Assert.Equal("data.output-contract-bindable", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("confirm_completion", finding.NodeId);
        Assert.Equal("output_contract_type", finding.FieldPath);
        Assert.Contains("additionalProperties", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutputContractBindable_UnknownContractName_LeavesItToTheResolveRule()
    {
        // Reporting an unknown name here too would double up on data.contract-types-resolve.
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a", outputContract: "NoSuchContract")]);

        var findings = await new OutputContractBindableRule(new ReflectionContractTypeCatalog(TestContractSet.Instance))
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public void OutputContractBindable_NullCatalog_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new OutputContractBindableRule(null!));

    [Fact]
    public async Task OutputContractBindable_NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new OutputContractBindableRule(new ReflectionContractTypeCatalog(TestContractSet.Instance)).EvaluateAsync(null!, CancellationToken.None));

    // ── the surface itself ──

    [Fact]
    public void ContractCatalogue_OffersOnlyBindableTypes_ButStillResolvesTheRest()
    {
        var catalogue = new ReflectionContractTypeCatalog(TestContractSet.Instance);

        Assert.DoesNotContain(nameof(DictionaryShapedProjection), catalogue.Names);
        Assert.Contains(nameof(UpdateResult), catalogue.Names);
        // Resolution stays broad: existing definitions and data edges must still resolve names.
        Assert.True(catalogue.Resolves(nameof(DictionaryShapedProjection)));
    }

    [Fact]
    public void ContractCatalogue_EveryOfferedType_IsActuallyBindable()
    {
        var catalogue = new ReflectionContractTypeCatalog(TestContractSet.Instance);

        var unusable = catalogue.Names
            .Select(name => (name, type: catalogue.Resolve(name)!))
            .Where(x => !StrictSchemaCheck.IsBindable(x.type))
            .Select(x => x.name)
            .ToList();

        Assert.True(unusable.Count == 0, $"offered but not bindable: {string.Join(", ", unusable)}");
    }

    [Fact]
    public void Schema_MarksNodeTypesTheRuntimeCannotExecute()
    {
        // The flag is the agent's only machine-readable signal; C-9 and the prompt both defer to it.
        var schema = new WorkflowSchemaProvider(new PhaseOneRuntime(), TestContractSet.Instance).GetSchema();

        var executable = schema.NodeTypes.Where(t => t.Executable).Select(t => t.NodeType).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(["agent_task", "human_gate"], executable);
        Assert.False(schema.NodeTypes.Single(t => t.NodeType == "parallel").Executable);
    }

    [Fact]
    public void Schema_PermissiveRuntime_MarksEveryNodeTypeExecutable()
    {
        var schema = new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance).GetSchema();

        Assert.All(schema.NodeTypes, t => Assert.True(t.Executable));
    }
}
