using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Unit tests for <see cref="AgentProposalParser"/> — tolerant JSON parsing of agent output (doc 14 §4).</summary>
public sealed class AgentProposalParserTests
{
    [Fact]
    public void TryParse_ValidJson_ReturnsProposalWithDefinition()
    {
        var raw = $$"""{"reason":"r","definition":{{MinimalDefinitionJson()}},"changed_node_ids":["n1"]}""";

        Assert.True(AgentProposalParser.TryParse(raw, out var proposal));
        Assert.Equal("r", proposal!.Reason);
        Assert.NotNull(proposal.Definition);
        Assert.Equal("n1", proposal.ChangedNodeIds![0]);
    }

    [Fact]
    public void TryParse_FencedJson_StripsFenceAndParses()
    {
        var raw = $"```json\n{{\"reason\":\"r\",\"definition\":{MinimalDefinitionJson()}}}\n```";

        Assert.True(AgentProposalParser.TryParse(raw, out var proposal));
        Assert.NotNull(proposal!.Definition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Just some prose, no JSON here.")]
    public void TryParse_NonJsonOrEmpty_ReturnsFalse(string? raw) =>
        Assert.False(AgentProposalParser.TryParse(raw, out _));

    [Fact]
    public void TryParse_JsonWithoutDefinition_ReturnsFalse() =>
        Assert.False(AgentProposalParser.TryParse("""{"reason":"r","changed_node_ids":[]}""", out _));

    // proposal?.Definition — every other test supplies a non-null deserialized proposal;
    // this exercises the null-conditional's short-circuit when the JSON literal itself is `null` (S9.24 branch-coverage gap).
    [Fact]
    public void TryParse_JsonLiteralNull_ReturnsFalse() =>
        Assert.False(AgentProposalParser.TryParse("null", out _));

    // S9.68: a node missing its `node_type` polymorphic discriminator (a common model mistake on a
    // fresh workflow with no example nodes to copy) throws NotSupportedException — not JsonException —
    // inside System.Text.Json. TryParse must catch it and degrade, not let it escape and 500 the turn.
    [Fact]
    public void TryParse_NodeMissingDiscriminator_ReturnsFalseWithoutThrowing()
    {
        var raw = $$"""{"reason":"r","definition":{{DefinitionJsonWithNodeMissingDiscriminator()}}}""";

        Assert.False(AgentProposalParser.TryParse(raw, out var proposal));
        Assert.Null(proposal);
    }

    [Fact]
    public void StripFences_NoFence_ReturnsTrimmedInput() =>
        Assert.Equal("{\"a\":1}", AgentProposalParser.StripFences("  {\"a\":1}  "));

    // lastFence >= 0 ? body[..lastFence] : body — every other fenced test supplies a closing fence;
    // this exercises the fallback when no closing ``` is found after the opening fence (S9.24 branch-coverage gap).
    [Fact]
    public void StripFences_UnclosedFence_ReturnsBodyAsIs() =>
        Assert.Equal("{\"a\":1}", AgentProposalParser.StripFences("```json\n{\"a\":1}"));

    [Fact]
    public void StripFences_FencedSingleLine_ReturnsInput() =>
        // A fence opener with no newline can't be stripped further; returns as-is (trimmed).
        Assert.Equal("```json {\"a\":1}", AgentProposalParser.StripFences("```json {\"a\":1}"));

    [Fact]
    public void StripFences_BareFence_ReturnsInnerJson() =>
        Assert.Equal("{\"a\":1}", AgentProposalParser.StripFences("```\n{\"a\":1}\n```"));

    private static string MinimalDefinitionJson()
    {
        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-x",
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test",
            Nodes = Array.Empty<WorkflowNode>(),
            Edges = Array.Empty<WorkflowEdge>(),
            DefinitionHash = "h",
            Mode = ExecutionMode.OneShot,
        };
        return JsonSerializer.Serialize(definition, CanonicalProfile.Options);
    }

    /// <summary>A definition whose single node has had its <c>node_type</c> discriminator stripped —
    /// exactly the shape a model emits when it forgets the discriminator.</summary>
    private static string DefinitionJsonWithNodeMissingDiscriminator()
    {
        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-x",
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test",
            Nodes = new WorkflowNode[]
            {
                new AgentTaskNode
                {
                    NodeId = "n1",
                    Role = "deep-reasoning",
                    InstructionsRef = "instr",
                    InputContractType = "In",
                    OutputContractType = "Out",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "e1",
                        AgentRole = "deep-reasoning",
                        BaselineComponents = Array.Empty<string>(),
                        DynamicFields = Array.Empty<string>(),
                    },
                },
            },
            Edges = Array.Empty<WorkflowEdge>(),
            DefinitionHash = "h",
            Mode = ExecutionMode.OneShot,
        };
        var json = JsonSerializer.Serialize(definition, CanonicalProfile.Options);
        // The discriminator is written as the first property of the node object.
        return json.Replace("\"node_type\":\"agent_task\",", "", StringComparison.Ordinal);
    }
}
