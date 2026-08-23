#pragma warning disable CA1034, CA1515 // Nested test classes group per-rule cases — the S930PureRuleTests precedent.
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7d (ADR-E2/ADR-CD9): the registry-era compiler rules — schema-ref format and
/// exact-id+major matching, the envelope ban on agent outputs, and the widened
/// <c>mcp.tool-resolves</c> covering the now-executable <c>McpToolNode</c>.
/// </summary>
public sealed class S137dRegistryRuleTests
{
    public sealed class DataSchemaRefMatchRuleTests
    {
        [Fact]
        public async Task ClrNamedEdges_AreIgnored()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a-1"), S930Fixtures.Agent("a-2")],
                edges: [S930Fixtures.Data("a-1", "a-2", "SummaryArtifact")]);

            Assert.Empty(await new DataSchemaRefMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task WellFormedRef_MatchingEndpointsWithDifferentMinors_ReturnsEmpty()
        {
            // ADR-E2 D3: minors may differ — 1.0 edge, 1.2 producer, 1.1 consumer all match.
            var definition = Definition(
                edgeRef: "schemas/document-structure/1.0",
                producerOut: "schemas/document-structure/1.2",
                consumerIn: "schemas/document-structure/1.1");

            Assert.Empty(await Evaluate(definition));
        }

        [Fact]
        public async Task MalformedEdgeRef_ReturnsFinding()
        {
            var definition = Definition(edgeRef: "schemas/document-structure/one.zero", producerOut: "SummaryArtifact", consumerIn: "SummaryArtifact");

            var finding = Assert.Single(await Evaluate(definition));
            Assert.Contains("not a well-formed schema ref", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MajorMismatch_ReturnsFinding()
        {
            var definition = Definition(
                edgeRef: "schemas/document-structure/2.0",
                producerOut: "schemas/document-structure/1.4",
                consumerIn: "schemas/document-structure/2.1");

            var finding = Assert.Single(await Evaluate(definition));
            Assert.Equal("output_contract_type", finding.FieldPath);
            Assert.Contains("exact schema id and major version", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task IdMismatch_ReturnsFindingPerEndpoint()
        {
            var definition = Definition(
                edgeRef: "schemas/document-structure/1.0",
                producerOut: "schemas/record-batch/1.0",
                consumerIn: "schemas/mapping-spec/1.0");

            var findings = await Evaluate(definition);

            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.FieldPath == "output_contract_type");
            Assert.Contains(findings, f => f.FieldPath == "input_contract_type");
        }

        [Fact]
        public async Task MalformedNodeRef_ReturnsFormatFinding()
        {
            var definition = Definition(
                edgeRef: "schemas/document-structure/1.0",
                producerOut: "schemas/Document_Structure/1.0", // uppercase + underscore: outside the convention
                consumerIn: "schemas/document-structure/1.0");

            var finding = Assert.Single(await Evaluate(definition));
            Assert.Contains("not a well-formed schema ref", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NonAgentEndpointsAndClrDeclaredEndpoints_AreSkipped()
        {
            // A schema-ref edge whose producer is an mcp_tool node (no declared contracts) and
            // whose consumer declares a CLR-named input: nothing to match, no findings —
            // and control edges never enter the rule at all.
            var definition = S930Fixtures.Build(
                [
                    S930Fixtures.Mcp("t-producer"),
                    S930Fixtures.Agent("a-consumer", inputContract: "SummaryArtifact"),
                ],
                edges:
                [
                    S930Fixtures.Control("t-producer", "a-consumer"),
                    S930Fixtures.Data("t-producer", "a-consumer", "schemas/record-batch/1.0"),
                    S930Fixtures.Data("a-consumer", "t-producer", "schemas/record-batch/1.0"), // mcp consumer: no declared input to match
                ]);

            Assert.Empty(await Evaluate(definition));
        }

        [Fact]
        public void ParseSchemaRef_RoundTripsTheConvention()
        {
            var parsed = DataSchemaRefMatchRule.ParseSchemaRef("io.frontier/record-batch/3.14");

            Assert.NotNull(parsed);
            Assert.Equal("io.frontier/record-batch", parsed!.Id);
            Assert.Equal(3, parsed.Major);
            Assert.Equal(14, parsed.Minor);
            Assert.Null(DataSchemaRefMatchRule.ParseSchemaRef("no-slashes"));
            Assert.False(DataSchemaRefMatchRule.IsSchemaRef("SummaryArtifact"));
            Assert.False(DataSchemaRefMatchRule.IsSchemaRef(null));
            Assert.True(DataSchemaRefMatchRule.IsSchemaRef("a/b/1.0"));
        }

        private static async Task<IReadOnlyList<ValidationFinding>> Evaluate(WorkflowDefinition definition) =>
            await new DataSchemaRefMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        private static WorkflowDefinition Definition(string edgeRef, string producerOut, string consumerIn) =>
            S930Fixtures.Build(
                [
                    S930Fixtures.Agent("a-producer", outputContract: producerOut),
                    S930Fixtures.Agent("a-consumer", inputContract: consumerIn),
                ],
                edges: [S930Fixtures.Data("a-producer", "a-consumer", edgeRef)]);
    }

    public sealed class AgentOutputNotEnvelopeRuleTests
    {
        [Fact]
        public async Task ConcreteOutputContract_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a-1", outputContract: "SummaryArtifact")]);

            Assert.Empty(await new AgentOutputNotEnvelopeRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task TypedPayloadOutput_ReturnsFinding()
        {
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a-1", outputContract: "TypedPayload")]);

            var finding = Assert.Single(await new AgentOutputNotEnvelopeRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("agent.output-not-envelope", finding.RuleId);
            Assert.Equal("output_contract_type", finding.FieldPath);
            Assert.Contains("ADR-AG1", finding.Message, StringComparison.Ordinal);
        }
    }

    public sealed class McpToolResolvesWideningTests
    {
        [Fact]
        public async Task McpToolNodeRef_Unknown_ReturnsFinding()
        {
            // S13.7c made mcp_tool designable; its ToolRef gets pinned-snapshot resolution too.
            var definition = S930Fixtures.Build([S930Fixtures.Mcp("t-1", toolRef: "io.frontier.demo/autotask/nonexistent")]);

            var finding = Assert.Single(await Evaluate(definition));
            Assert.Equal("mcp.tool-resolves", finding.RuleId);
            Assert.Equal("t-1", finding.NodeId);
        }

        [Fact]
        public async Task McpToolNodeRef_Known_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build([S930Fixtures.Mcp("t-1", toolRef: "io.frontier.demo/autotask/get_new_ticket")]);

            Assert.Empty(await Evaluate(definition));
        }

        private static async Task<IReadOnlyList<ValidationFinding>> Evaluate(WorkflowDefinition definition) =>
            await new McpToolResolvesRule(new StubToolCatalog()).EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        private sealed class StubToolCatalog : IDesignerToolCatalog
        {
            public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>(
                [
                    new DesignerToolDescriptor
                    {
                        ToolRef = "io.frontier.demo/autotask/get_new_ticket",
                        Server = "io.frontier.demo/autotask",
                        Name = "get_new_ticket",
                        Description = "Fetches a ticket.",
                    },
                ]);
        }
    }
}
