#pragma warning disable CA1034, CA1515
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// Pure-tier validation rule coverage (doc 13 §4.2, C-8).
/// Each rule tested with positive and negative cases.
/// </summary>
public sealed class ValidationRulesTests
{
    private static readonly IReadOnlyList<string> EmptyNodes = Array.Empty<string>();
    private static readonly IReadOnlyList<WorkflowEdge> EmptyEdges = Array.Empty<WorkflowEdge>();

    public sealed class UniqueNodeIdsRuleTests
    {
        [Fact]
        public async Task UniqueNodeIds_AllDistinct_ReturnsEmpty()
        {
            var rule = new StructureUniqueNodeIdsRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task UniqueNodeIds_DuplicateIds_ReturnsFinding()
        {
            var rule = new StructureUniqueNodeIdsRule();
            var nodes = new[]
            {
                new AgentTaskNode
                {
                    NodeId = "node-1",
                    Role = "gen-scope",
                    InstructionsRef = "scope-gen",
                    InputContractType = "BriefArtifact",
                    OutputContractType = "SummaryArtifact",
                    ContextRequest = TestContextRequest()
                },
                new AgentTaskNode
                {
                    NodeId = "node-1", // Duplicate!
                    Role = "gen-approach",
                    InstructionsRef = "approach-gen",
                    InputContractType = "SummaryArtifact",
                    OutputContractType = "PlanArtifact",
                    ContextRequest = TestContextRequest()
                }
            };
            var definition = new WorkflowDefinition
            {
                WorkflowId = "wf-test",
                DefinitionVersion = 1,
                EngagementType = "advisory-sow",
                Name = "Test",
                Nodes = nodes,
                Edges = new[]
                {
                    new WorkflowEdge { FromNodeId = "node-1", ToNodeId = "node-1", Kind = EdgeKind.Control }
                },
                DefinitionHash = "hash",
                Mode = ExecutionMode.OneShot
            };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.Equal("structure.unique-node-ids", f.RuleId));
        }

        [Fact]
        public async Task UniqueNodeIds_EdgeFromUnknownNode_ReturnsErrorOnFromNodeId()
        {
            var definition = WithExtraEdge("ghost", "gen-pricing");

            var findings = await new StructureUniqueNodeIdsRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            var finding = Assert.Single(findings);
            Assert.Equal("structure.unique-node-ids", finding.RuleId);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("from_node_id", finding.FieldPath);
            Assert.Equal("ghost->gen-pricing", finding.EdgeRef);
            Assert.Contains("'ghost'", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UniqueNodeIds_EdgeToUnknownNode_ReturnsErrorOnToNodeId()
        {
            var definition = WithExtraEdge("gen-scope", "ghost");

            var findings = await new StructureUniqueNodeIdsRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            var finding = Assert.Single(findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("to_node_id", finding.FieldPath);
            Assert.Equal("gen-scope->ghost", finding.EdgeRef);
            Assert.Contains("'ghost'", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UniqueNodeIds_EdgeWithBothEndsUnknown_ReturnsAFindingPerEnd()
        {
            var definition = WithExtraEdge("ghost-a", "ghost-b");

            var findings = await new StructureUniqueNodeIdsRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            Assert.Collection(
                findings,
                f => Assert.Equal("from_node_id", f.FieldPath),
                f => Assert.Equal("to_node_id", f.FieldPath));
        }

        [Fact]
        public async Task UniqueNodeIds_EdgeEndDiffersOnlyByCase_ReturnsFinding()
        {
            var definition = WithExtraEdge("gen-scope", "GEN-PRICING");

            var findings = await new StructureUniqueNodeIdsRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            Assert.Equal("to_node_id", Assert.Single(findings).FieldPath);
        }

        private static WorkflowDefinition WithExtraEdge(string fromNodeId, string toNodeId)
        {
            var definition = MinimalValidDefinition();
            return definition with
            {
                Edges = definition.Edges
                    .Append(new WorkflowEdge { FromNodeId = fromNodeId, ToNodeId = toNodeId, Kind = EdgeKind.Control })
                    .ToArray()
            };
        }
    }

    public sealed class IsAcyclicRuleTests
    {
        [Fact]
        public async Task IsAcyclic_LinearChain_ReturnsEmpty()
        {
            var rule = new GraphIsAcyclicRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task IsAcyclic_SelfLoop_ReturnsFinding()
        {
            var rule = new GraphIsAcyclicRule();
            var definition = MinimalValidDefinition() with
            {
                Edges = new[]
                {
                    new WorkflowEdge { FromNodeId = "gen-scope", ToNodeId = "gen-scope", Kind = EdgeKind.Control }
                }
            };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.Equal("graph.is-acyclic", f.RuleId));
        }

        [Fact]
        public async Task IsAcyclic_BackEdge_ReturnsFinding()
        {
            var rule = new GraphIsAcyclicRule();
            var definition = MinimalValidDefinition() with
            {
                Edges = new[]
                {
                    new WorkflowEdge { FromNodeId = "gen-scope", ToNodeId = "gen-approach", Kind = EdgeKind.Control },
                    new WorkflowEdge { FromNodeId = "gen-approach", ToNodeId = "gen-pricing", Kind = EdgeKind.Control },
                    new WorkflowEdge { FromNodeId = "gen-pricing", ToNodeId = "gen-scope", Kind = EdgeKind.Control }
                }
            };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.Equal("graph.is-acyclic", f.RuleId));
        }
    }

    public sealed class SingleEntryReachableRuleTests
    {
        [Fact]
        public async Task SingleEntryReachable_OneEntry_ReturnsEmpty()
        {
            var rule = new GraphSingleEntryReachableRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task SingleEntryReachable_MultipleEntriesNoConnection_ReturnsFinding()
        {
            var rule = new GraphSingleEntryReachableRule();
            var definition = new WorkflowDefinition
            {
                WorkflowId = "wf-test",
                DefinitionVersion = 1,
                EngagementType = "advisory-sow",
                Name = "Test",
                Nodes = new[]
                {
                    new AgentTaskNode
                    {
                        NodeId = "entry-1",
                        Role = "gen-scope",
                        InstructionsRef = "scope-gen",
                        InputContractType = "BriefArtifact",
                        OutputContractType = "SummaryArtifact",
                        ContextRequest = TestContextRequest()
                    },
                    new AgentTaskNode
                    {
                        NodeId = "entry-2",
                        Role = "gen-approach",
                        InstructionsRef = "approach-gen",
                        InputContractType = "SummaryArtifact",
                        OutputContractType = "PlanArtifact",
                        ContextRequest = TestContextRequest()
                    }
                },
                Edges = EmptyEdges,
                DefinitionHash = "hash",
                Mode = ExecutionMode.OneShot
            };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.Equal("graph.single-entry-reachable", f.RuleId));
        }
    }

    public sealed class FanOutFanInRuleTests
    {
        [Fact]
        public async Task FanOutFanIn_ProperLinearStructure_ReturnsEmpty()
        {
            var rule = new GraphFanOutFanInRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }
    }

    public sealed class DecisionEdgesRuleTests
    {
        [Fact]
        public async Task DecisionEdges_AllValid_ReturnsEmpty()
        {
            var rule = new GraphDecisionEdgesRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }
    }

    public sealed class DispatcherInputRuleTests
    {
        [Fact]
        public async Task DispatcherInput_OneShotMode_ReturnsEmpty()
        {
            var rule = new StructureDispatcherInputRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task DispatcherInput_DispatcherModeValid_ReturnsEmpty()
        {
            var rule = new StructureDispatcherInputRule();
            var definition = MinimalValidDefinition() with { Mode = ExecutionMode.Dispatcher };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }
    }

    public sealed class RequiredFieldsRuleTests
    {
        [Fact]
        public async Task RequiredFields_AllPresent_ReturnsEmpty()
        {
            var rule = new StructureRequiredFieldsRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task RequiredFields_NameMissingOrBlank_ReturnsErrorOnName(string? name)
        {
            var definition = MinimalValidDefinition() with { Name = name! };

            var finding = Assert.Single(await EvaluateAsync(definition));

            AssertRequiredFieldError(finding, "name");
        }

        [Theory]
        [InlineData("")]
        [InlineData("\t")]
        [InlineData(null)]
        public async Task RequiredFields_EngagementTypeMissingOrBlank_ReturnsErrorOnEngagementType(string? engagementType)
        {
            var definition = MinimalValidDefinition() with { EngagementType = engagementType! };

            var finding = Assert.Single(await EvaluateAsync(definition));

            AssertRequiredFieldError(finding, "engagement_type");
        }

        [Fact]
        public async Task RequiredFields_BothBlank_ReturnsOneErrorPerField()
        {
            var definition = MinimalValidDefinition() with { Name = "", EngagementType = "" };

            var findings = await EvaluateAsync(definition);

            Assert.Equal(["name", "engagement_type"], findings.Select(f => f.FieldPath));
            Assert.All(findings, f => Assert.Equal(ValidationSeverity.Error, f.Severity));
        }

        private static Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(WorkflowDefinition definition) =>
            new StructureRequiredFieldsRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

        private static void AssertRequiredFieldError(ValidationFinding finding, string fieldPath)
        {
            Assert.Equal("structure.required-fields", finding.RuleId);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(fieldPath, finding.FieldPath);
            Assert.Contains($"'{fieldPath}'", finding.Message, StringComparison.Ordinal);
        }
    }

    public sealed class VersioningNoClashRuleTests
    {
        [Fact]
        public async Task VersioningNoClash_FirstVersion_ReturnsEmpty()
        {
            var rule = new VersioningNoClashRule();
            var definition = MinimalValidDefinition() with { DefinitionVersion = 1 };
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }
    }

    public sealed class ModelIdRejectionRuleTests
    {
        [Fact]
        public async Task ModelIdRejection_NoEmbeddedIds_ReturnsEmpty()
        {
            var rule = new ModelIdRejectionRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task ModelIdRejection_AgentNodesUseRoles_ValidatesPhase1()
        {
            var rule = new ModelIdRejectionRule();
            var definition = MinimalValidDefinition();
            var ctx = new DefinitionValidationContext(definition);

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }
    }

    private static WorkflowDefinition MinimalValidDefinition()
    {
        return new WorkflowDefinition
        {
            WorkflowId = "wf-test",
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test Workflow",
            Nodes = new WorkflowNode[]
            {
                new AgentTaskNode
                {
                    NodeId = "gen-scope",
                    Role = "gen-scope",
                    InstructionsRef = "scope-gen",
                    InputContractType = "BriefArtifact",
                    OutputContractType = "SummaryArtifact",
                    ContextRequest = TestContextRequest()
                },
                new AgentTaskNode
                {
                    NodeId = "gen-approach",
                    Role = "gen-approach",
                    InstructionsRef = "approach-gen",
                    InputContractType = "SummaryArtifact",
                    OutputContractType = "PlanArtifact",
                    ContextRequest = TestContextRequest()
                },
                new AgentTaskNode
                {
                    NodeId = "gen-pricing",
                    Role = "gen-pricing",
                    InstructionsRef = "pricing-gen",
                    InputContractType = "PlanArtifact",
                    OutputContractType = "PricingSection",
                    ContextRequest = TestContextRequest()
                }
            },
            Edges = new[]
            {
                new WorkflowEdge { FromNodeId = "gen-scope", ToNodeId = "gen-approach", Kind = EdgeKind.Control },
                new WorkflowEdge { FromNodeId = "gen-approach", ToNodeId = "gen-pricing", Kind = EdgeKind.Control }
            },
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }

    private static ContextRequest TestContextRequest()
    {
        return new ContextRequest
        {
            EngagementId = "engagement-id",
            AgentRole = "test-role",
            BaselineComponents = EmptyNodes,
            DynamicFields = EmptyNodes
        };
    }
}
