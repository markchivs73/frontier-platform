
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// resilience.profile-exists (doc 13 §4.2 R2, doc 10 §4, S9.30): every node-level
/// <see cref="RetryPolicySpec.ProfileName"/> must name a profile in the resilience catalogue.
/// Also carries the resourced half of the tighten-only check (S9.30 split decision,
/// DESIGN-DECISIONS.md): overrides may never exceed the named profile's own caps.
/// </summary>
public sealed class ResilienceProfileExistsRule : IDefinitionValidationRule
{
    private readonly IRetryProfileCatalog _profiles;

    /// <summary>Constructs the rule over the resilience profile catalogue.</summary>
    public ResilienceProfileExistsRule(IRetryProfileCatalog profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
    }

    public string RuleId => "resilience.profile-exists";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var profiles = await _profiles.GetProfilesAsync(ct);
        var byId = profiles.ToDictionary(p => p.ProfileId, StringComparer.Ordinal);

        return ctx.Definition.Nodes
            .Where(node => node.Retry is not null)
            .SelectMany(node => NodeFindings(node, byId))
            .ToList();
    }

    private IEnumerable<ValidationFinding> NodeFindings(WorkflowNode node, Dictionary<string, RetryProfileDescriptor> byId)
    {
        if (!byId.TryGetValue(node.Retry!.ProfileName, out var profile))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"retry profile '{node.Retry.ProfileName}' does not exist in the resilience catalogue.",
                node.NodeId, FieldPath: "retry.profile_name");
            yield break;
        }

        if (node.Retry.MaxAttemptsOverride > profile.MaxAttempts)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"max_attempts_override {node.Retry.MaxAttemptsOverride} loosens profile '{profile.ProfileId}' (cap {profile.MaxAttempts}) — overrides may only tighten (doc 10 §4).",
                node.NodeId, FieldPath: "retry.max_attempts_override");
        }

        if (node.Retry.TimeoutSecondsOverride * 1000L > profile.TimeoutMs)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"timeout_seconds_override {node.Retry.TimeoutSecondsOverride}s loosens profile '{profile.ProfileId}' ({profile.TimeoutMs}ms) — overrides may only tighten (doc 10 §4).",
                node.NodeId, FieldPath: "retry.timeout_seconds_override");
        }
    }
}
