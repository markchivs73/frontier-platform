using System.Diagnostics.CodeAnalysis;


namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Evidence-based retirement monitoring: detects workflow versions with zero executions in an observation window.
/// Doc 13 §8: ADR-DC4 (retirement by evidence, not timers); Guardrails recommendation thresholds.
/// </summary>
public interface IRetirementMonitor
{
    /// <summary>Get retirement candidates: versions with zero executions in the observation window.</summary>
    Task<IReadOnlyList<RetirementCandidate>> GetCandidatesAsync(CancellationToken ct);
}

/// <summary>Workflow version eligible for retirement (zero executions in observation window).</summary>
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated equality")]
public sealed record RetirementCandidate
{
    /// <summary>Workflow ID.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Version number being considered for retirement.</summary>
    public required int Version { get; init; }

    /// <summary>When this version was last started for execution (UTC). Null if never executed.</summary>
    public DateTime? LastExecutionStartedUtc { get; init; }

    /// <summary>Count of executions in the observation window (typically zero for candidates).</summary>
    public required int ExecutionsInWindow { get; init; }

    /// <summary>Length of the observation window in days (default: 180 per ADR-DC4).</summary>
    public required int WindowDays { get; init; }

    /// <summary>Count of in-flight executions at retirement proposal time (executions started but not completed).</summary>
    public required int InFlightCount { get; init; }

    /// <summary>Version that superseded this one, if any. Null if version is still current or retired.</summary>
    public int? SupersededByVersion { get; init; }

    /// <summary>Recommendation severity: "safe" (no inflight), "monitor" (has inflight, safe after completion), "block" (reserved for edge cases).</summary>
    public required string RecommendationSeverity { get; init; }
}
