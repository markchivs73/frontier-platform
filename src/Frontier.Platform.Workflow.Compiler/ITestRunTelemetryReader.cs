
namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// S9.29g (doc 13 §5 "the test-run result... token/cost actuals... attaches to the
/// ValidationReport as advisory evidence"): a consumer-owned seam over the real, staged
/// per-invocation telemetry a sandbox test-run's agent invocations produce — the same
/// staging data the signed audit record's <c>AgentInvocations</c>/<c>CacheMetrics</c> are
/// built from for a real execution (doc 05 §4), read back here for a test-run instead.
/// <see cref="TestRunService"/> cannot reference <c>Frontier.Platform.Audit</c> directly
/// (library-boundaries) — a Host-side adapter implements this against the real staging
/// store and Model-Role Config's pricing, mirroring the <c>ITestRunExecutor</c> pattern.
/// </summary>
public interface ITestRunTelemetryReader
{
    /// <summary>
    /// Aggregates every staged invocation for <paramref name="executionId"/> into real
    /// token/cost totals. An execution with no staged invocations (e.g. a test-run blocked
    /// before it ever started, doc 13 §5's pure-tier gate) correctly resolves to an
    /// all-zero result — no special-casing needed at the call site.
    /// </summary>
    Task<TestRunCostMetrics> GetCostMetricsAsync(string executionId, CancellationToken ct);
}
