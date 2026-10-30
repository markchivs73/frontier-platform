using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-PA41: the designer copies a tool's registered contract refs and write flag onto its mcp_tool nodes.</summary>
public sealed class McpToolRegistryStampTests
{
    private const string Ticket = "io.frontier.demo/autotask/get_new_ticket";
    private const string Update = "io.frontier.demo/autotask/update_ticket";

    private static readonly DesignerToolDescriptor TicketTool = Tool(Ticket, "io.frontier.demo.autotask/get-new-ticket-input/1.0", "io.frontier.demo.autotask/get-new-ticket-output/1.0", false);

    [Fact]
    public void Apply_OverwritesWhateverTheNodeSaid_WithTheRegistrysValues()
    {
        var definition = Definition(Mcp("m", Ticket, "hand/written-in/1.0", "hand/written-out/1.0", true));

        var node = Single(McpToolRegistryStamp.Apply(definition, [TicketTool]));

        Assert.Equal("io.frontier.demo.autotask/get-new-ticket-input/1.0", node.InputContractType);
        Assert.Equal("io.frontier.demo.autotask/get-new-ticket-output/1.0", node.OutputContractType);
        Assert.False(node.Writes);
    }

    [Fact]
    public void Apply_CopiesTheRegistrysNulls_SoAToolWithNoOutputSchemaLeavesNoOutputRef()
    {
        var definition = Definition(Mcp("m", Update, "stale/in/1.0", "stale/out/1.0", false));

        var node = Single(McpToolRegistryStamp.Apply(definition, [Tool(Update, null, null, null)]));

        Assert.Null(node.InputContractType);
        Assert.Null(node.OutputContractType);
        Assert.Null(node.Writes);
    }

    [Fact]
    public void Apply_LeavesANodeWhoseToolDoesNotResolve_ForMcpToolResolvesToReport()
    {
        var unresolved = Mcp("m", "io.frontier.demo/autotask/no_such_tool", "hand/in/1.0", null, true);

        var node = Single(McpToolRegistryStamp.Apply(Definition(unresolved), [TicketTool]));

        Assert.Same(unresolved, node);
    }

    [Fact]
    public void Apply_LeavesEveryOtherNodeAndTheRestOfTheDefinitionAlone()
    {
        var agent = new AgentTaskNode
        {
            NodeId = "a",
            Role = "gen-scope",
            InstructionsRef = "scope-gen",
            InputContractType = "EngagementBriefSection",
            OutputContractType = "ScopeSection",
            ToolRefs = [Ticket],
            ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "gen-scope", BaselineComponents = [], DynamicFields = [] },
        };
        var definition = Definition(agent, Mcp("m", Ticket, null, null, null));

        var stamped = McpToolRegistryStamp.Apply(definition, [TicketTool]);

        Assert.Same(agent, stamped.Nodes[0]);
        Assert.Equal(["a", "m"], stamped.Nodes.Select(n => n.NodeId));
        Assert.Equal(definition with { Nodes = stamped.Nodes }, stamped);
        Assert.Null(((McpToolNode)definition.Nodes[1]).InputContractType);   // the input is not modified
    }

    [Fact]
    public void Apply_TakesTheFirstEntry_WhenTheCatalogueListsAToolRefTwice()
    {
        var node = Single(McpToolRegistryStamp.Apply(
            Definition(Mcp("m", Ticket, null, null, null)),
            [TicketTool, Tool(Ticket, "other/in/1.0", "other/out/1.0", true)]));

        Assert.Equal(TicketTool.InputContractRef, node.InputContractType);
        Assert.False(node.Writes);
    }

    [Fact]
    public void Apply_RefusesNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => McpToolRegistryStamp.Apply(null!, []));
        Assert.Throws<ArgumentNullException>(() => McpToolRegistryStamp.Apply(Definition(), null!));
    }

    internal static DesignerToolDescriptor Tool(string toolRef, string? input, string? output, bool? writes) => new()
    {
        ToolRef = toolRef,
        Server = "io.frontier.demo/autotask",
        Name = toolRef[(toolRef.LastIndexOf('/') + 1)..],
        Description = "A demo tool.",
        InputContractRef = input,
        OutputContractRef = output,
        Writes = writes,
    };

    internal static McpToolNode Mcp(string id, string toolRef, string? input, string? output, bool? writes) => new()
    {
        NodeId = id,
        ToolRef = toolRef,
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "engagementId",
        InputContractType = input,
        OutputContractType = output,
        Writes = writes,
    };

    internal static WorkflowDefinition Definition(params WorkflowNode[] nodes) => new()
    {
        WorkflowId = "wf-x",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Stamp test",
        Nodes = nodes,
        Edges = [],
        DefinitionHash = "test-hash",
        Mode = ExecutionMode.OneShot,
    };

    private static McpToolNode Single(WorkflowDefinition definition) => Assert.IsType<McpToolNode>(Assert.Single(definition.Nodes));
}
