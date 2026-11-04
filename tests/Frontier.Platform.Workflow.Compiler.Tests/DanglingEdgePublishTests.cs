using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.207 (defect D13): a definition whose edge names a node that is not there must fail
/// validation, and the publish proposal must refuse that report. Before, only the Model's
/// <c>WorkflowDefinition.Validate()</c> checked edge ends, and no publish path calls it.
/// </summary>
public sealed class DanglingEdgePublishTests
{
    private const string WorkflowId = "wf-dangling";
    private const string DraftRevision = "rev-1";

    [Fact]
    public async Task ValidateAsync_EdgeToMissingNode_FailsOnTheUniqueNodeIdsRule()
    {
        var validator = new DefinitionValidator([new StructureUniqueNodeIdsRule()]);

        var report = await validator.ValidateAsync(DanglingEdgeDefinition(), DraftRevision, storedSchemaVersion: null, CancellationToken.None);

        Assert.Equal(ValidationOutcome.Fail, report.Outcome);
        var finding = Assert.Single(report.Findings);
        Assert.Equal("structure.unique-node-ids", finding.RuleId);
        Assert.Equal("to_node_id", finding.FieldPath);
    }

    [Fact]
    public async Task ProposePublishAsync_ReportForAnEdgeToMissingNode_IsRefusedAndNotPersisted()
    {
        var definition = DanglingEdgeDefinition();
        var report = await new DefinitionValidator([new StructureUniqueNodeIdsRule()])
            .ValidateAsync(definition, DraftRevision, storedSchemaVersion: null, CancellationToken.None);
        var store = new Mock<IDefinitionStore>();
        store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(definition));
        var service = new DefinitionLifecycleService(store.Object, Mock.Of<IDefinitionCompiler>(), NoRegistryContracts.Embedder);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProposePublishAsync(WorkflowId, DraftRevision, report, "user:mark", CancellationToken.None));

        Assert.Contains("failing validation", ex.Message, StringComparison.Ordinal);
        store.Verify(s => s.PersistValidationReportAsync(It.IsAny<ValidationReportDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>One node and one edge from it to a node id no node carries.</summary>
    internal static WorkflowDefinition DanglingEdgeDefinition() => new()
    {
        WorkflowId = WorkflowId,
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Dangling edge",
        Nodes = [new AgentTaskNode
        {
            NodeId = "gen-scope",
            Role = "gen-scope",
            InstructionsRef = "scope-gen",
            InputContractType = "BriefArtifact",
            OutputContractType = "SummaryArtifact",
            ContextRequest = new ContextRequest { EngagementId = "engagement-id", AgentRole = "gen-scope", BaselineComponents = [], DynamicFields = [] }
        }],
        Edges = [new WorkflowEdge { FromNodeId = "gen-scope", ToNodeId = "ghost", Kind = EdgeKind.Control }],
        DefinitionHash = "",
        Mode = ExecutionMode.OneShot
    };

    private static DefinitionDraftDocument Draft(WorkflowDefinition definition) => new()
    {
        Id = $"{WorkflowId}:draft",
        WorkflowId = WorkflowId,
        State = "draft",
        BaseVersion = 1,
        DraftRevision = DraftRevision,
        Definition = definition,
        LastEditedBy = "user:sarah",
        LastEditedUtc = DateTime.UtcNow
    };
}
