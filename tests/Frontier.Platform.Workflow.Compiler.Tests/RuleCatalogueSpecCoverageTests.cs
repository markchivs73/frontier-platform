using Frontier.Platform.Workflow.Compiler.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.29b (Wave 9 planning, P1 process pilot): doc 13 §"Tests &amp; Validation" specifies a
/// checked-in expected-catalogue fixture — adding a rule without updating the fixture fails CI,
/// same pattern as the DI adjacency fixture, doc 12 §3. Existing governance (architecture tests,
/// byte-stability tests) catches violation of what exists; this test catches absence of what was
/// specified. S9.30 closed the register: all 26 doc-13 §4.2 catalogue rows are registered with
/// their documented ids, and the rules are resolved through the real
/// <c>AddFrontierWorkflowCompiler()</c> registration so the fixture can never drift from DI.
/// </summary>
public sealed class RuleCatalogueSpecCoverageTests
{
    /// <summary>
    /// Every rule <c>AddFrontierWorkflowCompiler()</c> registers, resolved through the real DI
    /// path with empty fakes for the Host-wired resource catalogs (S9.27c pattern).
    /// </summary>
    private static readonly IReadOnlyList<IDefinitionValidationRule> CurrentlyRegisteredRules = ResolveRegisteredRules();

    /// <summary>
    /// The full doc 13 §4.2 catalogue, canonical ids as documented. This is the checked-in
    /// expected-catalogue fixture: registering a rule the doc doesn't name, or renaming one,
    /// fails here — update the doc and this fixture deliberately, don't just make the test pass.
    /// </summary>
    private static readonly IReadOnlyList<string> DocumentedRuleCatalogue =
    [
        "structure.required-fields",
        "structure.dispatcher-input",
        "structure.unique-node-ids",
        "graph.single-entry-reachable",
        "graph.is-acyclic",
        "graph.fan-out-fan-in",
        "graph.decision-edges",
        "structure.node-type-supported",
        "determinism.predicates-compile",
        "data.contract-types-resolve",
        "data.edge-type-match",
        "data.single-data-predecessor",
        "data.schema-ref-match",
        "agent.output-not-envelope",
        "data.output-contract-bindable",
        "cascade.acyclic",
        "context.known-components",
        "context.baseline-scoped",
        "model-role.roles-exist",
        "model-role.no-model-ids",
        "agent.instructions-resolve",
        "mcp.tool-resolves",
        "mcp.write-idempotency",
        "hitl.approver-roles-exist",
        "hitl.rollback-target-valid",
        "resilience.profile-exists",
        "resilience.overrides-tighten-only",
        "timeouts.nesting",
        "retention.fits-window",
        "versioning.no-clash",
    ];

    [Fact]
    public void RegisteredRules_ExactlyMatchTheDocumentedCatalogue()
    {
        var actualIds = CurrentlyRegisteredRules.Select(r => r.RuleId).OrderBy(id => id, StringComparer.Ordinal);
        var expectedIds = DocumentedRuleCatalogue.OrderBy(id => id, StringComparer.Ordinal);

        Assert.Equal(expectedIds, actualIds);
    }

    [Fact]
    public void RegisteredRules_TierDistributionMatchesTheCatalogue()
    {
        // 17 Pure (9 original + 5 at S9.30 + data.single-data-predecessor at S13.7i +
        // data.schema-ref-match and agent.output-not-envelope at S13.7d/ADR-E2 — both pure:
        // format + cross-field semantics over the definition alone until registry schema-id
        // resolution activates), 13 Resourced (2 at S9.27c + 9 at S9.30 +
        // 2 at S13.7h: structure.node-type-supported and data.output-contract-bindable, both
        // resourced because they depend on deployment capability, not the definition alone),
        // and 0 Runtime — determinism.sample-eval was retired at S13.23. It was the tier's only
        // member, and nothing executes that tier, so it could never have produced a finding.
        Assert.Equal(17, CurrentlyRegisteredRules.Count(r => r.Tier == RuleTier.Pure));
        Assert.Equal(13, CurrentlyRegisteredRules.Count(r => r.Tier == RuleTier.Resourced));
        Assert.Equal(0, CurrentlyRegisteredRules.Count(r => r.Tier == RuleTier.Runtime));
    }

    [Fact]
    public void ResourcedTierRuleCatalogue_AllDocumentedRulesAreRegistered()
    {
        // Formerly skipped, tracked to S9.30 — un-skipping this test was S9.30's literal
        // Definition of Done. Every doc-13 §4.2 row must resolve to a registered rule.
        var registeredIds = CurrentlyRegisteredRules.Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);
        var missing = DocumentedRuleCatalogue.Where(id => !registeredIds.Contains(id)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void RegisteredRules_DeclareAValidDefaultSeverity()
    {
        // Severities are data a deployment may override (ADR-DC2) — every rule must declare a
        // well-formed default. Only retention.fits-window is non-Error by doc 13 §4.2 among the
        // registered set (determinism.sample-eval was the other, retired at S13.23).
        Assert.All(CurrentlyRegisteredRules, rule => Assert.True(Enum.IsDefined(rule.DefaultSeverity)));
        Assert.Equal(ValidationSeverity.Warning, CurrentlyRegisteredRules.Single(r => r.RuleId == "retention.fits-window").DefaultSeverity);
    }

    [Fact]
    public void NoRuleIsRegisteredIntoATierNothingExecutes()
    {
        // S13.23: DefinitionValidator runs Pure and Resourced. Nothing runs Runtime. A rule
        // registered there is silently inert — it appears in the catalogue, claims to govern
        // something, and can never produce a finding. That happened once and is easy to repeat,
        // because registering a rule looks identical whichever tier it declares.
        var inert = CurrentlyRegisteredRules.Where(r => r.Tier == RuleTier.Runtime).Select(r => r.RuleId).ToList();

        Assert.True(inert.Count == 0,
            $"Registered into RuleTier.Runtime, which nothing executes: {string.Join(", ", inert)}. "
            + "Either build the Runtime executor or register into a tier that runs.");
    }

    private static List<IDefinitionValidationRule> ResolveRegisteredRules()
    {
        var services = new ServiceCollection();
        // S13.12c (E16): the contract set is a composition-root registration — supply it here
        // exactly as the Host does, since the rules resolve through the real DI path.
        services.AddSingleton(TestContractSet.Instance);
        services.AddFrontierWorkflowCompiler();
        services.AddSingleton<IDesignerToolCatalog>(new EmptyToolCatalog());
        services.AddSingleton<IAgentRoleCatalog>(new EmptyAgentRoleCatalog());
        services.AddSingleton<IApproverRoleCatalog>(new EmptyApproverRoleCatalog());
        services.AddSingleton<IContextComponentCatalog>(new EmptyComponentCatalog());
        services.AddSingleton<IInstructionCatalog>(new EmptyInstructionCatalog());
        services.AddSingleton<IRetryProfileCatalog>(new EmptyProfileCatalog());
        services.AddSingleton<ICascadeGraphChecker>(new EmptyCascadeChecker());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return [.. scope.ServiceProvider.GetServices<IDefinitionValidationRule>()];
    }

    private sealed class EmptyToolCatalog : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>([]);
    }

    private sealed class EmptyAgentRoleCatalog : IAgentRoleCatalog
    {
        public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>([]);
    }

    private sealed class EmptyApproverRoleCatalog : IApproverRoleCatalog
    {
        public Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApproverRoleDescriptor>>([]);
    }

    private sealed class EmptyComponentCatalog : IContextComponentCatalog
    {
        public Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class EmptyInstructionCatalog : IInstructionCatalog
    {
        public Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class EmptyProfileCatalog : IRetryProfileCatalog
    {
        public Task<IReadOnlyList<RetryProfileDescriptor>> GetProfilesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RetryProfileDescriptor>>([]);
    }

    private sealed class EmptyCascadeChecker : ICascadeGraphChecker
    {
        public IReadOnlyList<string> CheckAtPublish(WorkflowDefinition definition) => [];
    }
}
