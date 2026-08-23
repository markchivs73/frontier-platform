#pragma warning disable CA1034, CA1515
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.30 pure-tier rule coverage (doc 13 §4.2): the four previously-hollow stub rules
/// (structure.dispatcher-input, graph.fan-out-fan-in, graph.decision-edges, versioning.no-clash)
/// and the five new pure rules (cascade.acyclic, context.baseline-scoped, mcp.write-idempotency,
/// hitl.rollback-target-valid, resilience.overrides-tighten-only). Positive and negative cases
/// per rule, mirroring <see cref="ResourcedTierRuleTests"/>'s convention.
/// </summary>
public sealed class S930PureRuleTests
{
    public sealed class StructureDispatcherInputRuleTests
    {
        [Fact]
        public async Task OneShotMode_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", inputContract: "")]));

            Assert.Empty(await new StructureDispatcherInputRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task DispatcherEntryAgentWithInputContract_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")], mode: ExecutionMode.Dispatcher));

            Assert.Empty(await new StructureDispatcherInputRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task DispatcherEntryAgentWithoutInputContract_ReturnsFinding()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", inputContract: " ")], mode: ExecutionMode.Dispatcher));

            var finding = Assert.Single(await new StructureDispatcherInputRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("structure.dispatcher-input", finding.RuleId);
            Assert.Equal("agent-1", finding.NodeId);
            Assert.Equal("input_contract_type", finding.FieldPath);
        }

        [Fact]
        public async Task DispatcherEntryNotAgentTask_ReturnsFinding()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Mcp("mcp-1")], mode: ExecutionMode.Dispatcher));

            var finding = Assert.Single(await new StructureDispatcherInputRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("mcp-1", finding.NodeId);
        }
    }

    public sealed class GraphFanOutFanInRuleTests
    {
        [Fact]
        public async Task BranchesConvergeAtJoin_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Parallel("par-1", ["a", "b"], "join-1"), S930Fixtures.Agent("a"), S930Fixtures.Agent("b"), S930Fixtures.Agent("join-1")],
                [S930Fixtures.Control("a", "join-1"), S930Fixtures.Control("b", "join-1")]);

            Assert.Empty(await new GraphFanOutFanInRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
        }

        [Fact]
        public async Task JoinNodeMissing_ReturnsSingleFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Parallel("par-1", ["a"], "missing-join"), S930Fixtures.Agent("a")]);

            var finding = Assert.Single(await new GraphFanOutFanInRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Equal("graph.fan-out-fan-in", finding.RuleId);
            Assert.Equal("join_node_id", finding.FieldPath);
        }

        [Fact]
        public async Task NoBranches_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Parallel("par-1", [], "join-1"), S930Fixtures.Agent("join-1")]);

            var finding = Assert.Single(await new GraphFanOutFanInRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Equal("branch_node_ids", finding.FieldPath);
        }

        [Fact]
        public async Task BranchMissing_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Parallel("par-1", ["ghost"], "join-1"), S930Fixtures.Agent("join-1")]);

            var finding = Assert.Single(await new GraphFanOutFanInRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Contains("ghost", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BranchDoesNotReachJoin_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Parallel("par-1", ["a"], "join-1"), S930Fixtures.Agent("a"), S930Fixtures.Agent("join-1")]);

            var finding = Assert.Single(await new GraphFanOutFanInRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Contains("does not converge", finding.Message, StringComparison.Ordinal);
        }
    }

    public sealed class GraphDecisionEdgesRuleTests
    {
        [Fact]
        public async Task ConditionedEdgesAndDefaultEdge_ReturnsEmpty()
        {
            // The data edge and the other node's control edge exercise the out-edge filter.
            var definition = S930Fixtures.Build(
                [S930Fixtures.Decision("dec-1", "fallback"), S930Fixtures.Agent("high"), S930Fixtures.Agent("fallback")],
                [
                    S930Fixtures.Control("dec-1", "high", condition: "budget > 1000"),
                    S930Fixtures.Control("dec-1", "fallback"),
                    S930Fixtures.Data("dec-1", "high", "SummaryArtifact"),
                    S930Fixtures.Control("high", "fallback"),
                ]);

            Assert.Empty(await new GraphDecisionEdgesRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
        }

        [Fact]
        public async Task NonDefaultEdgeWithoutCondition_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Decision("dec-1", "fallback"), S930Fixtures.Agent("high"), S930Fixtures.Agent("fallback")],
                [S930Fixtures.Control("dec-1", "high"), S930Fixtures.Control("dec-1", "fallback")]);

            var finding = Assert.Single(await new GraphDecisionEdgesRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Equal("graph.decision-edges", finding.RuleId);
            Assert.Equal("dec-1->high", finding.EdgeRef);
        }

        [Fact]
        public async Task NoEdgeToDefaultBranch_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Decision("dec-1", "fallback"), S930Fixtures.Agent("high"), S930Fixtures.Agent("fallback")],
                [S930Fixtures.Control("dec-1", "high", condition: "budget > 1000")]);

            var finding = Assert.Single(await new GraphDecisionEdgesRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Equal("default_branch_node_id", finding.FieldPath);
        }
    }

    public sealed class VersioningNoClashRuleTests
    {
        [Fact]
        public async Task PositiveVersion_ReturnsEmpty() =>
            Assert.Empty(await new VersioningNoClashRule().EvaluateAsync(
                Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")])), CancellationToken.None));

        [Fact]
        public async Task ZeroVersion_IsTheUnversionedDraftSentinel_ReturnsEmpty()
        {
            // S9.81: a from-scratch draft is minted at version 0; the real number is assigned at
            // publish. Flagging it here blocked test-runs and the agent-repair loop with an error
            // the designer could never resolve.
            var definition = S930Fixtures.Build([S930Fixtures.Agent("agent-1")]) with { DefinitionVersion = 0 };

            Assert.Empty(await new VersioningNoClashRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
        }

        [Fact]
        public async Task NegativeVersion_ReturnsFinding()
        {
            var definition = S930Fixtures.Build([S930Fixtures.Agent("agent-1")]) with { DefinitionVersion = -1 };

            var finding = Assert.Single(await new VersioningNoClashRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Equal("versioning.no-clash", finding.RuleId);
            Assert.Equal("definition_version", finding.FieldPath);
        }
    }

    public sealed class CascadeAcyclicRuleTests
    {
        [Fact]
        public async Task CheckerReturnsNoViolations_ReturnsEmpty()
        {
            var rule = new CascadeAcyclicRule(new FakeCascadeChecker());

            Assert.Empty(await rule.EvaluateAsync(Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")])), CancellationToken.None));
        }

        [Fact]
        public async Task CheckerReturnsViolations_MapsEachToAFinding()
        {
            var rule = new CascadeAcyclicRule(new FakeCascadeChecker("cycle a->b->a", "dangling section 'x'"));

            var findings = await rule.EvaluateAsync(Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")])), CancellationToken.None);

            Assert.Equal(2, findings.Count);
            Assert.All(findings, f => Assert.Equal("cascade.acyclic", f.RuleId));
            Assert.All(findings, f => Assert.Equal("cascade-logic", f.SourceLibrary));
        }

        [Fact]
        public void NullChecker_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new CascadeAcyclicRule(null!));

        private sealed class FakeCascadeChecker(params string[] violations) : ICascadeGraphChecker
        {
            public IReadOnlyList<string> CheckAtPublish(WorkflowDefinition definition) => violations;
        }
    }

    public sealed class ModelIdRejectionRuleTests
    {
        [Fact]
        public async Task NoModelIds_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")]));

            Assert.Empty(await new ModelIdRejectionRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task ModelIdInRoleField_ReturnsFinding()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", role: "claude-opus-4-8")]));

            var finding = Assert.Single(await new ModelIdRejectionRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("model-role.no-model-ids", finding.RuleId);
            Assert.Contains("claude-opus-4-8", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ModelIdBuriedInPromptTemplate_ReturnsFinding()
        {
            var gate = S930Fixtures.Gate("gate-1") with { PromptTemplate = "Compare against the gpt-4o baseline before approving." };
            var ctx = Ctx(S930Fixtures.Build([gate]));

            var finding = Assert.Single(await new ModelIdRejectionRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Contains("gpt-4o", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RepeatedModelId_ReturnsOneFinding()
        {
            var ctx = Ctx(S930Fixtures.Build(
                [S930Fixtures.Agent("a", role: "claude-opus-4-8"), S930Fixtures.Agent("b", role: "claude-opus-4-8")]));

            Assert.Single(await new ModelIdRejectionRule().EvaluateAsync(ctx, CancellationToken.None));
        }
    }

    public sealed class ContextBaselineScopedRuleTests
    {
        [Fact]
        public async Task NamedComponents_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", baselineComponents: ["firm-standards"])]));

            Assert.Empty(await new ContextBaselineScopedRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Theory]
        [InlineData("*")]
        [InlineData(" ")]
        public async Task WholeStoreOrBlankEntry_ReturnsFinding(string component)
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", baselineComponents: [component])]));

            var finding = Assert.Single(await new ContextBaselineScopedRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("context.baseline-scoped", finding.RuleId);
            Assert.Equal("agent-1", finding.NodeId);
        }
    }

    public sealed class McpWriteIdempotencyRuleTests
    {
        [Fact]
        public async Task SpecDeclared_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Mcp("mcp-1", idempotencyKeySpec: "ticket:{ticket_id}")]));

            Assert.Empty(await new McpWriteIdempotencyRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task BlankSpec_ReturnsFinding()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Mcp("mcp-1", idempotencyKeySpec: " ")]));

            var finding = Assert.Single(await new McpWriteIdempotencyRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("mcp.write-idempotency", finding.RuleId);
            Assert.Equal("idempotency_key_spec", finding.FieldPath);
        }
    }

    public sealed class HitlRollbackTargetValidRuleTests
    {
        [Fact]
        public async Task NullTarget_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Gate("gate-1")]));

            Assert.Empty(await new HitlRollbackTargetValidRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task UpstreamArtifactTarget_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("gen-scope", sectionKey: "scope"), S930Fixtures.Gate("gate-1", rollbackTo: "gen-scope")],
                [S930Fixtures.Control("gen-scope", "gate-1")]);

            Assert.Empty(await new HitlRollbackTargetValidRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
        }

        [Fact]
        public async Task MissingTarget_ReturnsSingleFinding()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Gate("gate-1", rollbackTo: "ghost")]));

            var finding = Assert.Single(await new HitlRollbackTargetValidRule().EvaluateAsync(ctx, CancellationToken.None));
            Assert.Equal("hitl.rollback-target-valid", finding.RuleId);
            Assert.Contains("ghost", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TargetWithoutArtifactKey_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("gen-scope", sectionKey: null), S930Fixtures.Gate("gate-1", rollbackTo: "gen-scope")],
                [S930Fixtures.Control("gen-scope", "gate-1")]);

            var finding = Assert.Single(await new HitlRollbackTargetValidRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Contains("no artifact_key", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TargetNotUpstream_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Gate("gate-1", rollbackTo: "downstream"), S930Fixtures.Agent("downstream", sectionKey: "scope")],
                [S930Fixtures.Control("gate-1", "downstream")]);

            var finding = Assert.Single(await new HitlRollbackTargetValidRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
            Assert.Contains("not upstream", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SelfTarget_ReturnsFindings()
        {
            // A gate rolling back to itself is both section-less and not-upstream.
            var definition = S930Fixtures.Build([S930Fixtures.Gate("gate-1", rollbackTo: "gate-1")]);

            var findings = await new HitlRollbackTargetValidRule().EvaluateAsync(Ctx(definition), CancellationToken.None);

            Assert.Contains(findings, f => f.Message.Contains("not upstream", StringComparison.Ordinal));
        }
    }

    public sealed class ResilienceOverridesTightenOnlyRuleTests
    {
        [Fact]
        public async Task NoRetrySpec_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1")]));

            Assert.Empty(await new ResilienceOverridesTightenOnlyRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task PositiveOverrides_ReturnsEmpty()
        {
            var retry = new RetryPolicySpec { ProfileName = "llm-default", MaxAttemptsOverride = 2, TimeoutSecondsOverride = 30 };
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", retry: retry)]));

            Assert.Empty(await new ResilienceOverridesTightenOnlyRule().EvaluateAsync(ctx, CancellationToken.None));
        }

        [Fact]
        public async Task NonPositiveOverrides_ReturnFindings()
        {
            var retry = new RetryPolicySpec { ProfileName = "llm-default", MaxAttemptsOverride = 0, TimeoutSecondsOverride = 0 };
            var ctx = Ctx(S930Fixtures.Build([S930Fixtures.Agent("agent-1", retry: retry)]));

            var findings = await new ResilienceOverridesTightenOnlyRule().EvaluateAsync(ctx, CancellationToken.None);

            Assert.Equal(2, findings.Count);
            Assert.All(findings, f => Assert.Equal("resilience.overrides-tighten-only", f.RuleId));
        }

        [Fact]
        public async Task ProfileNameWithoutOverrides_ReturnsEmpty()
        {
            var ctx = Ctx(S930Fixtures.Build(
                [S930Fixtures.Agent("agent-1", retry: new RetryPolicySpec { ProfileName = "llm-default" })]));

            Assert.Empty(await new ResilienceOverridesTightenOnlyRule().EvaluateAsync(ctx, CancellationToken.None));
        }
    }

    public sealed class DefinitionValidationContextTests
    {
        [Fact]
        public void CarriesDraftRevisionAndResourceVersions()
        {
            var versions = new Dictionary<string, string> { ["role-catalogue"] = "v3" };

            var ctx = new DefinitionValidationContext(S930Fixtures.Build([S930Fixtures.Agent("a")]), "rev-7", versions);

            Assert.Equal("rev-7", ctx.DraftRevision);
            Assert.Same(versions, ctx.ResourceVersions);
        }
    }

    private static DefinitionValidationContext Ctx(WorkflowDefinition definition) => new(definition);
}
