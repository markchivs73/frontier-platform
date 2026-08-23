using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Unit tests for <see cref="ProposalChangeSetBuilder"/> — the changeId vocabulary (doc 14 §4.1).</summary>
public sealed class ProposalChangeSetBuilderTests
{
    [Fact]
    public void Build_MapsEachDiffCategoryToChangeItems()
    {
        var diff = new WorkflowDefinitionDiff
        {
            NodesAdded = ["gate-1"],
            NodesRemoved = ["old-1"],
            NodesModified = ["scope"],
            EdgesAdded = ["scope→gate-1"],
            EdgesRemoved = ["a→b"],
            EdgesModified = [],
        };
        var from = Definition([Agent("old-1"), Agent("scope")]);
        var to = Definition([Gate("gate-1"), Agent("scope")]);

        var changes = ProposalChangeSetBuilder.Build(diff, from, to);

        Assert.Contains(changes, c => c.ChangeId == "node:added:gate-1" && c.ChangeType == "added" && c.NodeId == "gate-1" && c.NodeType == "human_gate");
        Assert.Contains(changes, c => c.ChangeId == "node:removed:old-1" && c.NodeId == "old-1" && c.NodeType == "agent_task");
        Assert.Contains(changes, c => c.ChangeId == "node:modified:scope" && c.NodeType == "agent_task");
        Assert.Contains(changes, c => c.ChangeId == "edge:added:scope→gate-1" && c.NodeId == null && c.NodeType == null);
        Assert.Contains(changes, c => c.ChangeId == "edge:removed:a→b");
        Assert.Equal(5, changes.Count);
    }

    [Fact]
    public void Build_RemovedNodeNoLongerInFrom_NodeTypeIsNull()
    {
        // S9.33: a diff can name a removed node id that isn't actually in `from` (e.g. a stale
        // reference) — the type lookup degrades to null rather than throwing.
        var diff = Empty() with { NodesRemoved = ["ghost"] };

        var changes = ProposalChangeSetBuilder.Build(diff, Definition([]), Definition([]));

        Assert.Null(Assert.Single(changes).NodeType);
    }

    [Fact]
    public void Build_DescriptionsAreHumanReadable()
    {
        var diff = Empty() with { NodesAdded = ["gate-1"], EdgesAdded = ["scope→gate-1"] };
        var to = Definition([Gate("gate-1")]);

        var changes = ProposalChangeSetBuilder.Build(diff, Definition([]), to);

        Assert.Equal("Add node 'gate-1'", changes.Single(c => c.ChangeId == "node:added:gate-1").Description);
        Assert.Equal("Add edge scope→gate-1", changes.Single(c => c.ChangeId == "edge:added:scope→gate-1").Description);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var diff = Empty();
        var def = Definition([]);
        Assert.Throws<ArgumentNullException>(() => ProposalChangeSetBuilder.Build(null!, def, def));
        Assert.Throws<ArgumentNullException>(() => ProposalChangeSetBuilder.Build(diff, null!, def));
        Assert.Throws<ArgumentNullException>(() => ProposalChangeSetBuilder.Build(diff, def, null!));
    }

    private static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes) => new()
    {
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Test",
        Nodes = nodes,
        Edges = [],
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    private static HumanGateNode Gate(string id) => new()
    {
        NodeId = id, GateKind = GateKind.Business, ApproverRoles = ["business-approver"],
        PromptTemplate = "Review", TimeoutMinutes = 0,
    };

    private static AgentTaskNode Agent(string id) => new()
    {
        NodeId = id, Role = "deep-reasoning", InstructionsRef = "instructions/x.md",
        InputContractType = "BriefArtifact", OutputContractType = "SummaryArtifact",
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1", AgentRole = "deep-reasoning",
            BaselineComponents = [], DynamicFields = [],
        },
    };


    [Theory]
    [InlineData("node:added:gate-1", "node", "added", "gate-1")]
    [InlineData("edge:added:scope→gate-1", "edge", "added", "scope→gate-1")]
    [InlineData("node:modified:scope", "node", "modified", "scope")]
    public void TryParse_ValidId_ReturnsParts(string id, string kind, string action, string reference)
    {
        Assert.True(ProposalChangeSetBuilder.TryParse(id, out var k, out var a, out var r));
        Assert.Equal((kind, action, reference), (k, a, r));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nocolons")]
    [InlineData("node:added:")]
    // secondColon < 0 — every other malformed case has zero or two colons; this has exactly one,
    // so firstColon is found but secondColon is not (S9.24 branch-coverage gap).
    [InlineData("node:added")]
    public void TryParse_Malformed_ReturnsFalse(string id) =>
        Assert.False(ProposalChangeSetBuilder.TryParse(id, out _, out _, out _));

    // action switch default arm (_ => action) — every other test uses Added/Removed/Modified;
    // this exercises the fallback for an unrecognised action string (S9.24 branch-coverage gap).
    [Fact]
    public void Capitalize_UnrecognisedAction_ReturnsActionUnchanged() =>
        Assert.Equal("weird", ProposalChangeSetBuilder.Capitalize("weird"));

    private static WorkflowDefinitionDiff Empty() => new()
    {
        NodesAdded = [],
        NodesRemoved = [],
        NodesModified = [],
        EdgesAdded = [],
        EdgesRemoved = [],
        EdgesModified = [],
    };
}
