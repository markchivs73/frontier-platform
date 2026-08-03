namespace Frontier.Platform.Guardrails;

/// <summary>
/// The compiled-in Phase 1 guardrail policy (doc 07 §9 layering, frozen to a single
/// platform-default policy for the PoC — engagement-type and per-engagement overrides
/// are S6.5 work). <see cref="AdmissionController"/> resolves <see cref="Default"/>
/// directly rather than a policy store, matching <c>Phase1ResilienceProfileCatalogue</c>'s
/// cold-start-fallback rationale (doc 10 §9).
/// </summary>
public static class Phase1GuardrailPolicyCatalogue
{
    /// <summary>
    /// The PoC Gate 3 (advisory SOW) policy: a 20,000-token / £2 per-invocation ceiling
    /// (doc 07 §4 worked example scale) for the three <c>deep-reasoning</c> agents
    /// (gen-scope/gen-approach/gen-pricing). <see cref="GuardrailPolicy.PerExecution"/>/
    /// <see cref="GuardrailPolicy.PerEngagement"/> are <c>null</c> (unbounded) until
    /// S6.5 implements hierarchical rollup.
    /// </summary>
    public static readonly GuardrailPolicy Default = new(
        PolicyId: "advisory-sow-default",
        PerInvocation: new BudgetSpec(MaxTokens: 20_000, MaxCostGbp: 2.00m, MaxAgentInvocations: null),
        PerExecution: null,
        PerEngagement: null);

    /// <summary>
    /// S9.38b (doc 13 §5 "Cost" row): the sandbox test-run policy — a smaller 5,000-token /
    /// £0.50 per-invocation ceiling than <see cref="Default"/>, confirmed with Mark (C-28,
    /// docs/IMPLEMENTATION-PLAN.md). <see cref="AdmissionController"/> selects this policy for
    /// any <c>SANDBOX-</c>-prefixed <see cref="InvocationCostEstimate.ExecutionId"/>. Only
    /// <see cref="BudgetSpec.MaxTokens"/> is actually enforced by <see cref="AdmissionController.Admit"/>
    /// today — <see cref="BudgetSpec.MaxCostGbp"/> is recorded but not read, matching
    /// <see cref="Default"/>'s existing (also-unenforced) field.
    /// </summary>
    public static readonly GuardrailPolicy Sandbox = new(
        PolicyId: "sandbox-test-run",
        PerInvocation: new BudgetSpec(MaxTokens: 5_000, MaxCostGbp: 0.50m, MaxAgentInvocations: null),
        PerExecution: null,
        PerEngagement: null);
}
