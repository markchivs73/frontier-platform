namespace Frontier.Platform.Guardrails;

/// <summary>
/// <see cref="IAdmissionController"/> over <see cref="Phase1GuardrailPolicyCatalogue"/>
/// (doc 07 §4, §5). Enforces only <see cref="GuardrailPolicy.PerInvocation"/> —
/// execution/engagement rollup is S6.5 (see <see cref="Phase1GuardrailPolicyCatalogue"/>).
/// </summary>
internal sealed class AdmissionController : IAdmissionController
{
    private const string SandboxExecutionIdPrefix = "SANDBOX-";

    /// <inheritdoc />
    public Task<AdmissionDecision> AdmitAsync(InvocationCostEstimate estimate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        return Task.FromResult(Admit(estimate, SelectPolicy(estimate.ExecutionId)));
    }

    /// <summary>
    /// S9.38b (doc 13 §5): a sandbox test-run's <c>SANDBOX-</c>-prefixed execution id
    /// (minted by S9.38a's <c>TestRunExecutorAdapter</c>) selects the smaller
    /// <see cref="Phase1GuardrailPolicyCatalogue.Sandbox"/> ceiling instead of the
    /// production default.
    /// </summary>
    internal static GuardrailPolicy SelectPolicy(string executionId) =>
        executionId.StartsWith(SandboxExecutionIdPrefix, StringComparison.Ordinal)
            ? Phase1GuardrailPolicyCatalogue.Sandbox
            : Phase1GuardrailPolicyCatalogue.Default;

    /// <summary>
    /// Applies <paramref name="policy"/>'s <see cref="GuardrailPolicy.PerInvocation"/>
    /// budget to <paramref name="estimate"/> (doc 07 §4): <see cref="AdmissionResult.Deny"/>
    /// if even zero output tokens would breach <see cref="BudgetSpec.MaxTokens"/>,
    /// <see cref="AdmissionResult.ProceedWithWarning"/> with a shaped
    /// <see cref="AdmissionDecision.GrantedMaxOutputTokens"/> if the requested output
    /// would breach it, else <see cref="AdmissionResult.Proceed"/> unchanged.
    /// </summary>
    internal static AdmissionDecision Admit(InvocationCostEstimate estimate, GuardrailPolicy policy)
    {
        if (policy.PerInvocation?.MaxTokens is not { } maxTokens)
        {
            return new AdmissionDecision(AdmissionResult.Proceed, null, estimate.MaxOutputTokens, null);
        }

        var remaining = maxTokens - estimate.PromptTokens;
        if (remaining <= 0)
        {
            return new AdmissionDecision(AdmissionResult.Deny, $"invocation token budget '{policy.PolicyId}' exhausted: {estimate.PromptTokens} prompt tokens >= {maxTokens} max", null, null);
        }

        if (remaining < estimate.MaxOutputTokens)
        {
            return new AdmissionDecision(AdmissionResult.ProceedWithWarning, $"invocation token budget '{policy.PolicyId}' shapes output to {remaining} tokens (requested {estimate.MaxOutputTokens})", remaining, null);
        }

        return new AdmissionDecision(AdmissionResult.Proceed, null, estimate.MaxOutputTokens, null);
    }
}
