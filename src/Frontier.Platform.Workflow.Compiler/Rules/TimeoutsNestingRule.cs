
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// timeouts.nesting (doc 13 §4.2 R3, doc 10 §7, S9.30): per-attempt timeout × attempts (the
/// retry pipeline) must fit inside the DTF activity timeout, so every layer times out before
/// the layer above kills it mid-cleanup — a definition can't ship what the Host's boot-time
/// <c>TimeoutHierarchyCheck</c> would reject. Phase 1 scope is the machine-timeout tiers only:
/// the HITL escalation tier (gate <c>timeout_minutes</c>) is an advisory notify, not a kill,
/// and is not compared — recorded in docs/state/SPEC-TRACEABILITY.md.
/// </summary>
public sealed class TimeoutsNestingRule : IDefinitionValidationRule
{
    /// <summary>The DTF activity timeout ceiling every retry pipeline must fit inside (doc 10 §7's 10-minute default).</summary>
    internal const long DtfActivityTimeoutMs = 600_000;

    private readonly IRetryProfileCatalog _profiles;

    /// <summary>Constructs the rule over the resilience profile catalogue.</summary>
    public TimeoutsNestingRule(IRetryProfileCatalog profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
    }

    public string RuleId => "timeouts.nesting";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var profiles = await _profiles.GetProfilesAsync(ct);
        var byId = profiles.ToDictionary(p => p.ProfileId, StringComparer.Ordinal);

        return RetryPipelineFindings(ctx.Definition, byId).Concat(McpTimeoutFindings(ctx.Definition)).ToList();
    }

    private IEnumerable<ValidationFinding> RetryPipelineFindings(
        WorkflowDefinition definition, Dictionary<string, RetryProfileDescriptor> byId)
    {
        // Unresolvable profiles are resilience.profile-exists findings, not duplicated here.
        foreach (var node in definition.Nodes.Where(n => n.Retry is not null && byId.ContainsKey(n.Retry.ProfileName)))
        {
            var profile = byId[node.Retry!.ProfileName];
            var attemptMs = node.Retry.TimeoutSecondsOverride * 1000L ?? profile.TimeoutMs;
            var attempts = node.Retry.MaxAttemptsOverride ?? profile.MaxAttempts;

            if (attemptMs * attempts > DtfActivityTimeoutMs)
            {
                yield return new ValidationFinding(RuleId, DefaultSeverity,
                    $"retry pipeline ({attempts} × {attemptMs}ms) exceeds the DTF activity timeout ({DtfActivityTimeoutMs}ms) — the layer above would kill it mid-cleanup (doc 10 §7).",
                    node.NodeId, FieldPath: "retry");
            }
        }
    }

    private IEnumerable<ValidationFinding> McpTimeoutFindings(WorkflowDefinition definition) =>
        definition.Nodes.OfType<McpToolNode>()
            .Where(node => node.TimeoutSeconds * 1000L > DtfActivityTimeoutMs)
            .Select(node => new ValidationFinding(RuleId, DefaultSeverity,
                $"timeout_seconds {node.TimeoutSeconds}s exceeds the DTF activity timeout ({DtfActivityTimeoutMs}ms) (doc 10 §7).",
                node.NodeId, FieldPath: "timeout_seconds"));
}
