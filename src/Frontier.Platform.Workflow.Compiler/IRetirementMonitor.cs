using System.Diagnostics.CodeAnalysis;


namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The evidence supplier doc 13 §8 designs for ADR-DC4 (retirement by evidence, not timers): versions
/// with zero executions in an observation window. <b>Not built.</b> The query is deferred (S13.223,
/// Mark's decision 2026-11-05) and the platform registers no implementation. <i>Trigger:</i> build it
/// when the governance dashboard's retirement-candidate surface (doc 13 §8 step 2) is scheduled;
/// until then retirement is a <c>workflow-admin</c> decision taken without candidate evidence.
/// </summary>
[Obsolete(RetirementMonitorDeferral.Message)]
public interface IRetirementMonitor
{
    /// <summary>The designed contract: versions with zero executions in the observation window. Not implemented.</summary>
    Task<IReadOnlyList<RetirementCandidate>> GetCandidatesAsync(CancellationToken ct);
}

/// <summary>Workflow version eligible for retirement (zero executions in observation window). Nothing produces one: see <see cref="IRetirementMonitor"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated equality")]
[Obsolete(RetirementMonitorDeferral.Message)]
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
