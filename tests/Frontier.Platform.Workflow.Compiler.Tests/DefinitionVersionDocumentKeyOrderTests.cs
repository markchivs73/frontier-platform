using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// ADR-PA19 / consumer S13.64: the 2026-09 Cosmos emulator's query path returns every object's keys
/// in Postgres <c>jsonb</c> order — shortest first, then bytewise, <c>id</c> last — so an
/// <see cref="AgentTaskNode"/>'s <c>node_type</c> comes back third. <c>SELECT *</c> over
/// <see cref="DefinitionVersionDocument"/> must still produce the same definition.
/// </summary>
public sealed class DefinitionVersionDocumentKeyOrderTests
{
    /// <summary>The canonical profile with only ADR-PA19's flag turned back off — the control differs from the real options in exactly the thing under test.</summary>
    private static readonly JsonSerializerOptions ProfileWithoutOutOfOrderMetadata = new(CanonicalProfile.Options) { AllowOutOfOrderMetadataProperties = false };

    private static DefinitionVersionDocument Document() => new()
    {
        Id = "wf-typed:v1",
        WorkflowId = "wf-typed",
        State = "published",
        DefinitionVersion = 1,
        DefinitionHash = "sha256:abc",
        Definition = new WorkflowDefinition
        {
            WorkflowId = "wf-typed",
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Typed",
            Nodes = [new AgentTaskNode
            {
                NodeId = "n1",
                Role = "deep-reasoning",
                InstructionsRef = "instr",
                InputContractType = "In",
                OutputContractType = "Out",
                ContextRequest = new ContextRequest { EngagementId = "e1", AgentRole = "deep-reasoning", BaselineComponents = [], DynamicFields = [] },
            }],
            Edges = [],
            DefinitionHash = "",
            Mode = ExecutionMode.OneShot,
        },
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        ApprovedUtc = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        ValidationReportRef = "ref",
    };

    /// <summary>Reorders every object's keys exactly as the emulator's query path does: shortest first, then bytewise, <c>id</c> last.</summary>
    internal static JsonNode ApplyJsonbKeyOrder(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var pairs = obj.Select(p => (p.Key, Value: p.Value is null ? null : ApplyJsonbKeyOrder(p.Value))).ToList();
                foreach (var key in pairs.Select(p => p.Key)) obj.Remove(key);
                foreach (var (key, value) in pairs.OrderBy(p => p.Key == "id" ? 1 : 0).ThenBy(p => p.Key.Length).ThenBy(p => p.Key, StringComparer.Ordinal)) obj[key] = value;
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) if (arr[i] is { } child) ApplyJsonbKeyOrder(child);
                return arr;
            default:
                return node;
        }
    }

    [Fact]
    public void JsonbOrder_MovesTheNodeDiscriminatorOffFirstPosition()
    {
        var reordered = ApplyJsonbKeyOrder(JsonNode.Parse(CanonicalProfile.SerializeCanonical(Document()))!);
        var node = (JsonObject)reordered["definition"]!["nodes"]![0]!;

        Assert.Equal("role", node.First().Key);
        Assert.NotEqual("node_type", node.First().Key);
    }

    [Fact]
    public void Read_JsonbOrderedDocument_YieldsTheSameDefinitionAndTypedNodes()
    {
        var canonical = CanonicalProfile.SerializeCanonical(Document());
        var reordered = ApplyJsonbKeyOrder(JsonNode.Parse(canonical)!).ToJsonString();

        var read = JsonSerializer.Deserialize<DefinitionVersionDocument>(reordered, CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.IsType<AgentTaskNode>(Assert.Single(read.Definition.Nodes));
        Assert.Equal(canonical, CanonicalProfile.SerializeCanonical(read));
    }

    [Fact]
    public void Read_JsonbOrderedDocument_WouldThrowUnderProfileWithoutOutOfOrderMetadata()
    {
        var reordered = ApplyJsonbKeyOrder(JsonNode.Parse(CanonicalProfile.SerializeCanonical(Document()))!).ToJsonString();

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<DefinitionVersionDocument>(reordered, ProfileWithoutOutOfOrderMetadata));
    }
}
