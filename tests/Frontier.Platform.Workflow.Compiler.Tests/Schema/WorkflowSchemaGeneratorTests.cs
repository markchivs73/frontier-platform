using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Schema;

namespace Frontier.Platform.Workflow.Compiler.Tests.Schema;

/// <summary>
/// Unit tests for <see cref="WorkflowSchemaGenerator"/> — reflection over Abstractions into the
/// design-language schema (doc 14 §7). Structural assertions use an empty doc reader for
/// determinism; one test exercises the real doc file for descriptions.
/// </summary>
public sealed class WorkflowSchemaGeneratorTests
{
    private static readonly string[] ExpectedNodeTypes =
    [
        "agent_task", "human_gate", "decision", "parallel",
        "loop", "mcp_tool", "context_injection", "cascade_check",
    ];

    [Fact]
    public void Generate_PopulatesVersionAndAllArtifacts()
    {
        var schema = GenerateWithoutDocs();

        Assert.Equal("1.0", schema.SchemaVersion);
        Assert.Equal(8, schema.NodeTypes.Count);
        Assert.Equal(6, schema.Enums.Count); // + ComparisonOp/LogicalOp at S13.7j (doc 14 §6 predicate trees)
        Assert.NotEmpty(schema.Edge.Fields);
        Assert.NotEmpty(schema.Contracts);
    }

    [Fact]
    public void BuildNodeTypes_IncludesAllEightDiscriminators()
    {
        var present = GenerateWithoutDocs().NodeTypes.Select(n => n.NodeType);

        Assert.Equal(ExpectedNodeTypes.OrderBy(x => x), present.OrderBy(x => x));
    }

    [Fact]
    public void BuildContracts_ListsIVersionedContractTypes_ExcludesNonContracts()
    {
        var contracts = GenerateWithoutDocs().Contracts;

        // Known concrete IVersionedContract implementers appear...
        Assert.Contains("LookupResult", contracts);
        Assert.Contains("MatchRequest", contracts);
        // ...the interface/node/edge types do not.
        Assert.DoesNotContain("IVersionedContract", contracts);
        Assert.DoesNotContain("WorkflowNode", contracts);
        Assert.DoesNotContain("WorkflowEdge", contracts);
        // Sorted ordinally so the wire order (and the prompt) is stable.
        Assert.Equal(contracts.OrderBy(c => c, StringComparer.Ordinal), contracts);
    }

    [Fact]
    public void ContextInjection_IsDeprecated_AllOthersAreNot()
    {
        var schema = GenerateWithoutDocs();

        Assert.True(Node(schema, "context_injection").Deprecated);
        Assert.All(
            schema.NodeTypes.Where(n => n.NodeType != "context_injection"),
            n => Assert.False(n.Deprecated));
    }

    [Fact]
    public void AgentTask_Fields_HaveExpectedTokensRequiredFlagsAndBaseFieldOrder()
    {
        var agent = Node(GenerateWithoutDocs(), "agent_task");

        // Inherited base fields come first, in JsonPropertyOrder.
        Assert.Equal(["node_id", "artifact_key", "retry"], agent.Fields.Take(3).Select(f => f.Name));

        AssertField(agent, "node_id", "string", required: true);
        AssertField(agent, "artifact_key", "string", required: false);
        AssertField(agent, "retry", "object:RetryPolicySpec", required: false);
        AssertField(agent, "role", "string", required: true);
        AssertField(agent, "context_request", "object:ContextRequest", required: true);
    }

    [Fact]
    public void AgentTask_Fields_IncludeToolRefs()
    {
        // S9.25/ADR-CD7: AgentTaskNode.ToolRefs must surface automatically — the schema
        // generator reflects every JsonPropertyName-carrying property with no allowlist, so
        // this is a regression guard, not new generator behaviour.
        var agent = Node(GenerateWithoutDocs(), "agent_task");

        AssertField(agent, "tool_refs", "array<string>", required: false);
    }

    [Fact]
    public void HumanGate_Fields_MapEnumListIntegerAndBoolTokens()
    {
        var gate = Node(GenerateWithoutDocs(), "human_gate");

        AssertField(gate, "gate_kind", "enum:GateKind", required: true);
        AssertField(gate, "approver_roles", "array<string>", required: true);
        AssertField(gate, "timeout_minutes", "integer", required: true);
        AssertField(gate, "reapprove_on_cascade", "boolean", required: false);
    }

    [Fact]
    public void Edge_Fields_MapEnumAndOptionalContractType()
    {
        var edge = GenerateWithoutDocs().Edge;

        AssertField(edge.Fields, "from_node_id", "string", required: true);
        AssertField(edge.Fields, "kind", "enum:EdgeKind", required: true);
        AssertField(edge.Fields, "contract_type", "string", required: false);
    }

    [Theory]
    [InlineData("GateKind", "intake", "business", "technical")]
    [InlineData("EdgeKind", "control", "data")]
    [InlineData("ExecutionMode", "one_shot", "dispatcher")]
    public void BuildEnums_ListsCanonicalValues(string enumName, params string[] expected)
    {
        var descriptor = GenerateWithoutDocs().Enums.Single(e => e.Name == enumName);

        Assert.Equal(expected, descriptor.Values);
    }

    [Fact]
    public void NodeTypeEnum_ListsAllEightDiscriminators()
    {
        var nodeTypeEnum = GenerateWithoutDocs().Enums.Single(e => e.Name == "NodeType");

        Assert.Equal(ExpectedNodeTypes.OrderBy(x => x), nodeTypeEnum.Values.OrderBy(x => x));
    }

    [Fact]
    public void Objects_ExpandContextRequestFieldsWithRequiredFlags()
    {
        var contextRequest = GenerateWithoutDocs().Objects.Single(o => o.Name == "ContextRequest");

        AssertField(contextRequest.Fields, "engagement_id", "string", required: true);
        AssertField(contextRequest.Fields, "agent_role", "string", required: true);
        AssertField(contextRequest.Fields, "baseline_components", "array<string>", required: true);
        AssertField(contextRequest.Fields, "dynamic_fields", "array<string>", required: true);
        AssertField(contextRequest.Fields, "requires_real_time", "boolean", required: false);
    }

    [Fact]
    public void Objects_IncludeEveryComplexContractReferencedByNodes()
    {
        var names = GenerateWithoutDocs().Objects.Select(o => o.Name).ToList();

        // The two complex contracts referenced by node fields (AgentTask.context_request, WorkflowNode.retry).
        Assert.Contains("ContextRequest", names);
        Assert.Contains("RetryPolicySpec", names);
    }

    [Fact]
    public void EmptyDocReader_LeavesDescriptionsNull()
    {
        var agent = Node(GenerateWithoutDocs(), "agent_task");

        Assert.Null(agent.Description);
        Assert.Null(Field(agent, "role").Description);
    }

    [Fact]
    public void RealDocFile_PopulatesNodeAndFieldDescriptions()
    {
        var generator = new WorkflowSchemaGenerator(XmlDocReader.ForAssembly(typeof(WorkflowNode).Assembly), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance);

        var agent = Node(generator.Generate(), "agent_task");

        Assert.False(string.IsNullOrWhiteSpace(agent.Description));
        Assert.Contains("agent role", Field(agent, "role").Description!, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFields_SkipsNonWirePropertiesAndToleratesMissingOrder()
    {
        var generator = new WorkflowSchemaGenerator(new XmlDocReader(new Dictionary<string, string>()), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance);

        var fields = generator.BuildFields(typeof(FieldProbe));

        // Only the property carrying a JsonPropertyName is emitted; the unattributed one is skipped.
        Assert.Single(fields);
        Assert.Equal("x", fields[0].Name);
    }

    // GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? int.MaxValue — LINQ's OrderBy skips
    // invoking the key selector entirely when only one element survives the Where filter (no sort is
    // needed), so a single-property probe type never actually reaches this line. TwoFieldProbe has two
    // wire properties, one missing [JsonPropertyOrder], forcing OrderBy to compare keys and exercise
    // the fallback (S9.24 branch-coverage gap: line 64 had 0 hits under an isolated single-test run).
    [Fact]
    public void BuildFields_MultiplePropertiesOneMissingOrder_SortsUnorderedPropertyLast()
    {
        var generator = new WorkflowSchemaGenerator(new XmlDocReader(new Dictionary<string, string>()), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance);

        var fields = generator.BuildFields(typeof(TwoFieldProbe));

        Assert.Equal(2, fields.Count);
        Assert.Equal("first", fields[0].Name);
        Assert.Equal("unordered", fields[1].Name);
    }

    [Fact]
    public void BuildObjects_ExpandsNestedComplexTypesViaClosure()
    {
        var generator = new WorkflowSchemaGenerator(new XmlDocReader(new Dictionary<string, string>()), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance);

        var objects = generator.BuildObjects(new HashSet<Type> { typeof(OuterProbe) });

        Assert.Contains(objects, o => o.Name == "OuterProbe");
        Assert.Contains(objects, o => o.Name == "InnerProbe"); // discovered transitively
    }

    /// <summary>Probe type exercising the generator's property filter and missing-order fallback.</summary>
    private sealed record FieldProbe
    {
        public string? Ignored { get; init; }

        [JsonPropertyName("x")]
        public string? X { get; init; }
    }

    /// <summary>Probe type with two wire properties, one lacking [JsonPropertyOrder], so OrderBy
    /// must actually compare keys (a single-property source skips the key selector entirely).</summary>
    private sealed record TwoFieldProbe
    {
        [JsonPropertyName("first")]
        [JsonPropertyOrder(0)]
        public string? First { get; init; }

        [JsonPropertyName("unordered")]
        public string? Unordered { get; init; }
    }

    /// <summary>Probes exercising BuildObjects' closure over nested complex types.</summary>
    private sealed record OuterProbe
    {
        [JsonPropertyName("inner")]
        public InnerProbe? Inner { get; init; }
    }

    private sealed record InnerProbe
    {
        [JsonPropertyName("v")]
        public string? V { get; init; }
    }

    private static WorkflowSchema GenerateWithoutDocs() =>
        new WorkflowSchemaGenerator(new XmlDocReader(new Dictionary<string, string>()), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance).Generate();

    private static NodeTypeDescriptor Node(WorkflowSchema schema, string nodeType) =>
        schema.NodeTypes.Single(n => n.NodeType == nodeType);

    private static FieldDescriptor Field(NodeTypeDescriptor node, string name) =>
        node.Fields.Single(f => f.Name == name);

    private static void AssertField(NodeTypeDescriptor node, string name, string type, bool required) =>
        AssertField(node.Fields, name, type, required);

    private static void AssertField(IReadOnlyList<FieldDescriptor> fields, string name, string type, bool required)
    {
        var field = fields.Single(f => f.Name == name);
        Assert.Equal(type, field.Type);
        Assert.Equal(required, field.Required);
    }
}
