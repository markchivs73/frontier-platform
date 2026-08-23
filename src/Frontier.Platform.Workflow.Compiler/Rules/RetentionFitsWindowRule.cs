
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// The deployment's DTF history retention window the <c>retention.fits-window</c> rule compares
/// against. The 30-day default is the S9.30 decision (DESIGN-DECISIONS.md); real per-deployment
/// values are ADR-A1 configuration landing at S10.1 (C-11).
/// </summary>
public sealed record RetentionWindowConfig
{
    /// <summary>DTF history retention in days; estimated workflow duration must fit inside it.</summary>
    public int DtfRetentionDays { get; init; } = 30;
}

/// <summary>
/// retention.fits-window (doc 13 §4.2, ADR-A1, S9.30 — Warning): the definition's estimated
/// duration (gate escalation timeouts plus a DTF-activity-ceiling allowance per non-gate node)
/// must fit the deployment's DTF retention window, or a slow execution's history could be
/// purged mid-run. The estimate is deliberately coarse — gates dominate real durations.
/// </summary>
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
