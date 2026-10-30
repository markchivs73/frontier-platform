#pragma warning disable CA1034, CA1515
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.30 resourced/runtime-tier rule coverage (doc 13 §4.2): the eight rules validating against
/// real resource catalogues plus the two determinism rows. Positive and negative cases per rule,
/// mirroring <see cref="ResourcedTierRuleTests"/>'s convention.
/// </summary>
public sealed class S930ResourcedRuleTests
{
    public sealed class DataContractTypesResolveRuleTests
    {
        [Fact]
        public async Task AllContractsResolve_ReturnsEmpty()
        {
            var rule = new DataContractTypesResolveRule(new FakeContracts("BriefArtifact", "SummaryArtifact"));
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "SummaryArtifact")],
                [S930Fixtures.Data("a", "b", "SummaryArtifact")]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnresolvableDataEdgeContract_ReturnsFindingWithEdgeRef()
        {
            var rule = new DataContractTypesResolveRule(new FakeContracts("BriefArtifact", "SummaryArtifact"));
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "BriefArtifact")],
                [S930Fixtures.Data("a", "b", "GhostContract")]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("data.contract-types-resolve", finding.RuleId);
            Assert.Equal("a->b", finding.EdgeRef);
        }

        [Fact]
        public async Task DataEdgeWithoutContractType_ReturnsFinding()
        {
            var rule = new DataContractTypesResolveRule(new FakeContracts("BriefArtifact", "SummaryArtifact"));
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "BriefArtifact")],
                [S930Fixtures.Data("a", "b", null)]);

            Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnresolvableNodeContracts_ReturnFindingsPerField()
        {
            var rule = new DataContractTypesResolveRule(new FakeContracts());
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a")]);

            var findings = await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.FieldPath == "input_contract_type");
            Assert.Contains(findings, f => f.FieldPath == "output_contract_type");
        }

        [Fact]
        public void NullCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new DataContractTypesResolveRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new DataContractTypesResolveRule(new FakeContracts()).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class ReflectionContractTypeCatalogTests
    {
        [Fact]
        public void ResolvesRealContract_AndRejectsUnknown()
        {
            var catalog = new ReflectionContractTypeCatalog(TestContractSet.Instance);

            Assert.True(catalog.Resolves("SummaryArtifact"));
            Assert.True(catalog.Resolves("BriefArtifact"));
            Assert.False(catalog.Resolves("NotAContractType"));
        }

        [Fact]
        public void Resolve_KnownAndUnknownContractType_ReturnsClrTypeOrNull()
        {
            var catalog = new ReflectionContractTypeCatalog(TestContractSet.Instance);

            Assert.Equal(typeof(SummaryArtifact), catalog.Resolve("SummaryArtifact"));
            Assert.Null(catalog.Resolve("NotAContractType"));
        }
    }

    public sealed class DataEdgeTypeMatchRuleTests
    {
        [Fact]
        public async Task EdgeMatchesConsumerInput_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "SummaryArtifact")],
                [S930Fixtures.Data("a", "b", "SummaryArtifact")]);

            Assert.Empty(await new DataEdgeTypeMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task EdgeMismatchesConsumerInput_ReturnsFinding()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "PricingSection")],
                [S930Fixtures.Data("a", "b", "SummaryArtifact")]);

            var finding = Assert.Single(await new DataEdgeTypeMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("data.edge-type-match", finding.RuleId);
            Assert.Equal("b", finding.NodeId);
            Assert.Equal("a->b", finding.EdgeRef);
        }

        [Fact]
        public async Task ConsumerNotAgentTask_ReturnsEmpty()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Gate("gate-1")],
                [S930Fixtures.Data("a", "gate-1", "SummaryArtifact")]);

            Assert.Empty(await new DataEdgeTypeMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task EdgeWithoutContractType_LeftToContractTypesResolveRule()
        {
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a"), S930Fixtures.Agent("b", inputContract: "PricingSection")],
                [S930Fixtures.Data("a", "b", null)]);

            Assert.Empty(await new DataEdgeTypeMatchRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }
    }

    public sealed class ContextKnownComponentsRuleTests
    {
        [Fact]
        public async Task KnownComponentsAndFields_ReturnsEmpty()
        {
            var rule = new ContextKnownComponentsRule(new FakeComponents(["firm-standards"], ["engagement_brief"]));
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a", baselineComponents: ["firm-standards"], dynamicFields: ["engagement_brief"])]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnknownBaselineComponent_ReturnsFinding()
        {
            var rule = new ContextKnownComponentsRule(new FakeComponents(["firm-standards"], []));
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", baselineComponents: ["ghost-component"])]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("context.known-components", finding.RuleId);
            Assert.Equal("context_request.baseline_components", finding.FieldPath);
        }

        [Fact]
        public async Task UnknownDynamicField_ReturnsFinding()
        {
            var rule = new ContextKnownComponentsRule(new FakeComponents([], []));
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", dynamicFields: ["ghost_field"])]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("context_request.dynamic_fields", finding.FieldPath);
        }

        [Fact]
        public async Task WholeStoreEntry_LeftToBaselineScopedRule()
        {
            var rule = new ContextKnownComponentsRule(new FakeComponents([], []));
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", baselineComponents: ["*"])]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public void NullCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new ContextKnownComponentsRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new ContextKnownComponentsRule(new FakeComponents([], [])).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class AgentInstructionsResolveRuleTests
    {
        private static AgentInstructionsResolveRule Rule(params string[] refs) =>
            new(new FakeInstructions(refs), new FakeRoles(("deep-reasoning", RoleBacking.Model), ("ticket-triage", RoleBacking.Agent)));

        private static async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(AgentInstructionsResolveRule rule, AgentTaskNode node) =>
            await rule.EvaluateAsync(new DefinitionValidationContext(S930Fixtures.Build([node])), CancellationToken.None);

        [Fact]
        public async Task RefResolves_ReturnsEmpty()
        {
            Assert.Empty(await EvaluateAsync(Rule("instructions/gen-scope.md"), S930Fixtures.Agent("a")));
        }

        [Fact]
        public async Task RefUnresolved_ReturnsFinding()
        {
            var finding = Assert.Single(await EvaluateAsync(Rule(), S930Fixtures.Agent("a", instructionsRef: "instructions/ghost.md")));

            Assert.Equal("agent.instructions-resolve", finding.RuleId);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("instructions_ref", finding.FieldPath);
            Assert.Contains("'instructions/ghost.md' does not resolve", finding.Message, StringComparison.Ordinal);
        }

        /// <summary>ADR-PA40: the agent brings its own instructions, so its node may omit the ref.</summary>
        [Fact]
        public async Task AgentBackedRole_WithNoRef_ReturnsEmpty()
        {
            var node = S930Fixtures.Agent("a", role: "ticket-triage") with { InstructionsRef = null };

            Assert.Empty(await EvaluateAsync(Rule(), node));
        }

        [Fact]
        public async Task AgentBackedRole_WithAnUnresolvedRef_StillReturnsFinding()
        {
            var node = S930Fixtures.Agent("a", role: "ticket-triage", instructionsRef: "instructions/ghost.md");

            Assert.Single(await EvaluateAsync(Rule(), node));
        }

        [Fact]
        public async Task ModelBackedRole_WithNoRef_IsAnError()
        {
            var node = S930Fixtures.Agent("a") with { InstructionsRef = null };

            var finding = Assert.Single(await EvaluateAsync(Rule("instructions/gen-scope.md"), node));
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("a", finding.NodeId);
            Assert.Equal("instructions_ref", finding.FieldPath);
            Assert.Contains("role 'deep-reasoning' is not agent-backed", finding.Message, StringComparison.Ordinal);
        }

        /// <summary>An unknown role is <c>model-role.roles-exist</c>'s; here it gets the stricter, model-backed reading.</summary>
        [Fact]
        public async Task UnknownRole_WithNoRef_IsAnError()
        {
            var node = S930Fixtures.Agent("a", role: "ghost-role") with { InstructionsRef = null };

            Assert.Single(await EvaluateAsync(Rule(), node));
        }

        [Theory]
        [InlineData("deep-reasoning", "")]
        [InlineData("ticket-triage", "")]
        [InlineData("ticket-triage", "   ")]
        public async Task BlankRef_IsAnErrorOnAnyRole(string role, string blank)
        {
            var finding = Assert.Single(await EvaluateAsync(Rule(blank), S930Fixtures.Agent("a", role: role, instructionsRef: blank)));

            Assert.Contains("instructions_ref is blank", finding.Message, StringComparison.Ordinal);
        }

        /// <summary>ADR-PA40 at publish: the validator fails a model-backed node with no ref and passes an agent-backed one.</summary>
        [Theory]
        [InlineData("deep-reasoning", false)]
        [InlineData("ticket-triage", true)]
        public async Task Publish_RefusesAMissingRefOnlyOnAModelBackedRole(string role, bool publishable)
        {
            var validator = new DefinitionValidator([Rule()]);
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", role: role) with { InstructionsRef = null }]);

            var report = await validator.ValidateAsync(definition, "rev-1", storedSchemaVersion: null, CancellationToken.None);

            Assert.Equal(publishable, report.IsPublishable);
        }

        [Fact]
        public void NullInstructionCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new AgentInstructionsResolveRule(null!, new FakeRoles()));

        [Fact]
        public void NullRoleCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new AgentInstructionsResolveRule(new FakeInstructions(), null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() => Rule().EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class HitlApproverRolesExistRuleTests
    {
        [Fact]
        public async Task RoleExists_ReturnsEmpty()
        {
            var rule = new HitlApproverRolesExistRule(new FakeApproverRoles("business-approver"));
            var definition = S930Fixtures.Build([S930Fixtures.Gate("gate-1")]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnknownRole_ReturnsFinding()
        {
            var rule = new HitlApproverRolesExistRule(new FakeApproverRoles("business-approver"));
            var definition = S930Fixtures.Build([S930Fixtures.Gate("gate-1", approverRoles: ["ghost-approver"])]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("hitl.approver-roles-exist", finding.RuleId);
            Assert.Equal("gate-1", finding.NodeId);
        }

        [Fact]
        public void NullCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new HitlApproverRolesExistRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new HitlApproverRolesExistRule(new FakeApproverRoles()).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class ResilienceProfileExistsRuleTests
    {
        [Fact]
        public async Task ProfileExistsWithinCaps_ReturnsEmpty()
        {
            var rule = new ResilienceProfileExistsRule(new FakeProfiles(("llm-default", 3, 90_000)));
            var retry = new RetryPolicySpec { ProfileName = "llm-default", MaxAttemptsOverride = 2, TimeoutSecondsOverride = 60 };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnknownProfile_ReturnsSingleFinding()
        {
            var rule = new ResilienceProfileExistsRule(new FakeProfiles(("llm-default", 3, 90_000)));
            var retry = new RetryPolicySpec { ProfileName = "ghost-profile", MaxAttemptsOverride = 99 };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("resilience.profile-exists", finding.RuleId);
            Assert.Equal("retry.profile_name", finding.FieldPath);
        }

        [Fact]
        public async Task OverridesLoosenProfileCaps_ReturnFindings()
        {
            var rule = new ResilienceProfileExistsRule(new FakeProfiles(("llm-default", 3, 90_000)));
            var retry = new RetryPolicySpec { ProfileName = "llm-default", MaxAttemptsOverride = 5, TimeoutSecondsOverride = 120 };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            var findings = await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

            Assert.Equal(2, findings.Count);
            Assert.All(findings, f => Assert.Contains("loosens", f.Message, StringComparison.Ordinal));
        }

        [Fact]
        public async Task NoOverrides_ProfileNameOnly_ReturnsEmpty()
        {
            var rule = new ResilienceProfileExistsRule(new FakeProfiles(("llm-default", 3, 90_000)));
            var definition = S930Fixtures.Build(
                [S930Fixtures.Agent("a", retry: new RetryPolicySpec { ProfileName = "llm-default" })]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public void NullCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new ResilienceProfileExistsRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new ResilienceProfileExistsRule(new FakeProfiles()).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class TimeoutsNestingRuleTests
    {
        [Fact]
        public async Task PipelineFitsActivityTimeout_ReturnsEmpty()
        {
            var rule = new TimeoutsNestingRule(new FakeProfiles(("llm-default", 3, 90_000)));
            var retry = new RetryPolicySpec { ProfileName = "llm-default" };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry), S930Fixtures.Mcp("mcp-1")]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task PipelineExceedsActivityTimeout_ReturnsFinding()
        {
            var rule = new TimeoutsNestingRule(new FakeProfiles(("slow-profile", 5, 300_000)));
            var retry = new RetryPolicySpec { ProfileName = "slow-profile" };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("timeouts.nesting", finding.RuleId);
            Assert.Contains("exceeds the DTF activity timeout", finding.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TightenedOverridesBringPipelineWithinTimeout_ReturnsEmpty()
        {
            // Profile alone would exceed (5 × 300s); the node's tightened overrides fit.
            var rule = new TimeoutsNestingRule(new FakeProfiles(("slow-profile", 5, 300_000)));
            var retry = new RetryPolicySpec { ProfileName = "slow-profile", MaxAttemptsOverride = 2, TimeoutSecondsOverride = 60 };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task UnknownProfile_SkippedHere()
        {
            var rule = new TimeoutsNestingRule(new FakeProfiles());
            var retry = new RetryPolicySpec { ProfileName = "ghost-profile" };
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a", retry: retry)]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task McpTimeoutExceedsActivityTimeout_ReturnsFinding()
        {
            var rule = new TimeoutsNestingRule(new FakeProfiles());
            var definition = S930Fixtures.Build([S930Fixtures.Mcp("mcp-1", timeoutSeconds: 700)]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("timeout_seconds", finding.FieldPath);
        }

        [Fact]
        public void NullCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new TimeoutsNestingRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new TimeoutsNestingRule(new FakeProfiles()).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class RetentionFitsWindowRuleTests
    {
        [Fact]
        public async Task EstimateFitsDefaultWindow_ReturnsEmpty()
        {
            var rule = new RetentionFitsWindowRule(new RetentionWindowConfig());
            var definition = S930Fixtures.Build([S930Fixtures.Agent("a"), S930Fixtures.Gate("gate-1", timeoutMinutes: 120)]);

            Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task EstimateExceedsWindow_ReturnsWarning()
        {
            var rule = new RetentionFitsWindowRule(new RetentionWindowConfig { DtfRetentionDays = 1 });
            var definition = S930Fixtures.Build([S930Fixtures.Gate("gate-1", timeoutMinutes: 2 * 24 * 60)]);

            var finding = Assert.Single(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("retention.fits-window", finding.RuleId);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        }

        [Fact]
        public void NullConfig_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new RetentionFitsWindowRule(null!));

        [Fact]
        public async Task NullContext_Throws() =>
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new RetentionFitsWindowRule(new RetentionWindowConfig()).EvaluateAsync(null!, CancellationToken.None));
    }

    public sealed class DeterminismPredicatesCompileRuleTests
    {
        [Fact]
        public async Task LoopBoundsValid_ReturnsEmpty()
        {
            // S13.7j: decision-tree validation moved to real structural checks — the
            // branch-tree cases live in S137jPredicateRuleTests; this suite keeps the
            // loop-bound half.
            var definition = S930Fixtures.Build([S930Fixtures.Loop("loop-1")]);

            Assert.Empty(await new DeterminismPredicatesCompileRule(new ReflectionContractTypeCatalog(TestContractSet.Instance)).EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        }

        [Fact]
        public async Task BranchlessDecision_ReturnsFinding()
        {
            // The deprecated string predicate is never evaluated (S13.7j) — a decision
            // without a branch tree is unexecutable and must fail validation.
            var definition = S930Fixtures.Build([S930Fixtures.Decision("dec-1", "fallback")]);

            var finding = Assert.Single(await new DeterminismPredicatesCompileRule(new ReflectionContractTypeCatalog(TestContractSet.Instance)).EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("determinism.predicates-compile", finding.RuleId);
            Assert.Equal("branches", finding.FieldPath);
        }

        [Fact]
        public async Task NonPositiveLoopBound_ReturnsFinding()
        {
            var definition = S930Fixtures.Build([S930Fixtures.Loop("loop-1", maxIterations: 0)]);

            var finding = Assert.Single(await new DeterminismPredicatesCompileRule(new ReflectionContractTypeCatalog(TestContractSet.Instance)).EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.Equal("max_iterations", finding.FieldPath);
        }
    }

    private sealed class FakeContracts(params string[] names) : IContractTypeCatalog
    {
        public bool Resolves(string contractTypeName) => names.Contains(contractTypeName, StringComparer.Ordinal);
        public Type? Resolve(string contractTypeName) => null;
        public IReadOnlyList<string> Names => names;
    }

    private sealed class FakeComponents(
        IReadOnlyCollection<string> baseline, IReadOnlyCollection<string> dynamicFields) : IContextComponentCatalog
    {
        public Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct) => Task.FromResult(baseline);
        public Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct) => Task.FromResult(dynamicFields);
    }

    private sealed class FakeInstructions(params string[] refs) : IInstructionCatalog
    {
        public Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct) =>
            Task.FromResult(refs.Contains(instructionsRef, StringComparer.Ordinal));
        public Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(refs);
    }

    private sealed class FakeRoles(params (string RoleId, RoleBacking Backing)[] roles) : IAgentRoleCatalog
    {
        public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>(
                [.. roles.Select(r => new AgentRoleDescriptor { RoleId = r.RoleId, Description = r.RoleId, Backing = r.Backing })]);
    }

    private sealed class FakeApproverRoles(params string[] roleIds) : IApproverRoleCatalog
    {
        public Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApproverRoleDescriptor>>(
                [.. roleIds.Select(id => new ApproverRoleDescriptor
                {
                    RoleId = id,
                    DisplayName = id,
                    Description = "fake role",
                    Responsibilities = [],
                    ApplicableGateKinds = [],
                })]);
    }

    private sealed class FakeProfiles(params (string Id, int MaxAttempts, int TimeoutMs)[] profiles) : IRetryProfileCatalog
    {
        public Task<IReadOnlyList<RetryProfileDescriptor>> GetProfilesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RetryProfileDescriptor>>(
                [.. profiles.Select(p => new RetryProfileDescriptor { ProfileId = p.Id, MaxAttempts = p.MaxAttempts, TimeoutMs = p.TimeoutMs })]);
    }
}
