namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// S9.53 (doc 19 A4-R4 "per-step expansion", doc 13 §6): a consumer-owned seam over the
/// artifact-state store's read side — reads back the real output a completed test-run node
/// wrote to its section, so the A4 result panel can show what each node actually produced.
/// <see cref="TestRunService"/> cannot reference <c>Frontier.Reason.Workflow.ArtifactState</c>
/// directly (library-boundaries) — a Host-side adapter implements this against the real
/// <c>IArtifactStateStore</c>, mirroring the <see cref="ITestRunTelemetryReader"/> and
/// <c>ITestRunExecutor</c> patterns.
/// </summary>
public interface ITestRunArtifactReader
{
    /// <summary>
    /// The current content of <paramref name="sectionKey"/> for the given sandbox execution,
    /// or <see langword="null"/> when the section was never written or has expired under the
    /// sandbox run's TTL (the panel then renders "no output for this node").
    /// </summary>
    Task<string?> GetArtifactContentAsync(string executionId, string engagementId, string sectionKey, CancellationToken ct);
}
