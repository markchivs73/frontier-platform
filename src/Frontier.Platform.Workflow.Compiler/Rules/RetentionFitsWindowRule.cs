
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// The deployment's DTF history retention window the retired <c>retention.fits-window</c> rule
/// compared against. Nothing in the compiler reads it any more: see <see cref="RetentionFitsWindowRule"/>.
/// </summary>
[Obsolete(RetentionFitsWindowRetirement.Message)]
public sealed record RetentionWindowConfig
{
    /// <summary>DTF history retention in days; estimated workflow duration must fit inside it.</summary>
    public int DtfRetentionDays { get; init; } = 30;
}

/// <summary>
/// retention.fits-window (doc 13 §4.2, ADR-A1, S9.30 — Warning), retired at S13.139: it compared
/// a definition's estimated duration (gate timeouts plus an allowance per non-gate node) with the
/// DTF retention window, on the premise that a slow run's history could be purged mid-run. The
/// Durable Task Scheduler's autopurge removes only runs in a terminal state, and its retention
/// clock starts when a run finishes, so a run's length is never compared with retention and the
/// rule's only output was a false warning on long-gated workflows. <c>AddFrontierWorkflowCompiler</c>
/// no longer registers it; the class still evaluates as it did for a caller that constructs it.
/// </summary>
[Obsolete(RetentionFitsWindowRetirement.Message)]
public sealed class RetentionFitsWindowRule : IDefinitionValidationRule
{
    private readonly RetentionWindowConfig _config;

    /// <summary>Constructs the rule over the deployment's retention window configuration.</summary>
    public RetentionFitsWindowRule(RetentionWindowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    public string RuleId => "retention.fits-window";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var gateMinutes = ctx.Definition.Nodes.OfType<HumanGateNode>().Sum(g => (long)g.TimeoutMinutes);
        var activityAllowanceMinutes = ctx.Definition.Nodes.Count(n => n is not HumanGateNode) * 10L;
        var windowMinutes = _config.DtfRetentionDays * 24L * 60L;

        IReadOnlyList<ValidationFinding> findings = gateMinutes + activityAllowanceMinutes <= windowMinutes
            ? []
            : [new ValidationFinding(RuleId, DefaultSeverity,
                $"estimated duration ({gateMinutes + activityAllowanceMinutes} min of gate timeouts + activity allowance) exceeds the deployment's DTF retention window ({_config.DtfRetentionDays} days).")];

        return Task.FromResult(findings);
    }
}

/// <summary>The deprecation text shared by the retired retention rule and its configuration.</summary>
internal static class RetentionFitsWindowRetirement
{
    /// <summary>Why the rule is obsolete, what replaces it, and when it goes.</summary>
    internal const string Message =
        "Deprecated in v0.51.0: retention.fits-window warned about a risk that does not exist (S13.139). Durable Task "
        + "Scheduler autopurge removes only terminal runs and starts its clock when a run finishes, so no definition's "
        + "duration can outrun retention. No compiler replacement: the check that matters (retention outlasts the "
        + "recovery sweep interval plus the consolidation settle window, ADR-A1) is a deployment boot check in the "
        + "consumer's host, which owns both values. Removed at the next phase boundary.";
}
