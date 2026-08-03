namespace Frontier.Platform.Observability;

/// <summary>
/// Records doc 12 §8 recovery-sweep findings as the <c>recovery.findings</c> OTEL
/// counter (C-22), tagged by <see cref="RecoveryFindingType"/>. A healthy platform
/// shows ~0 across all finding types — non-zero counts are what
/// <c>/health/governance</c> (S6.9) and OTEL dashboards alert on.
/// </summary>
public interface IRecoveryFindingsRecorder
{
    /// <summary>Increments <c>recovery.findings</c> for <paramref name="findingType"/>.</summary>
    void RecordFinding(RecoveryFindingType findingType);
}
