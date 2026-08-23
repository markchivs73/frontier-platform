
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <see cref="IAuditConsolidator"/> implementing doc 05 §4's consolidation algorithm:
/// joins the final <see cref="ExecutionSnapshot"/> (DTF-side evidence — completed steps,
/// human decisions, status) with staged <see cref="AuditTelemetryRecord"/>s (MAF-side
/// evidence — agent invocations, cache metrics, C-15) into one unsigned
/// <see cref="AuditRecord"/>. <see cref="AuditRecord.ValidatorOutcomes"/> is always
/// <c>[]</c> — no validators exist until Stage 6.
/// </summary>
internal sealed class AuditConsolidator(IExecutionSnapshotReader snapshotReader, IAuditTelemetryStaging telemetryStaging) : IAuditConsolidator
{
    /// <inheritdoc />
    public async Task<AuditRecord> ConsolidateAsync(ConsolidateAuditInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (engagementId, workflowId) = ExecutionIdParser.Parse(input.ExecutionId);
        var snapshot = await snapshotReader.GetLatestAsync(input.ExecutionId, engagementId, cancellationToken)
            ?? throw new InvalidOperationException($"No execution snapshot found for '{input.ExecutionId}' (doc 05 §4 step 4 requires the final checkpoint).");
        var telemetry = await telemetryStaging.GetForExecutionAsync(input.ExecutionId, cancellationToken);

        return BuildAuditRecord(input, engagementId, workflowId, snapshot, telemetry);
    }

    private const string SandboxEngagementIdPrefix = "SANDBOX-";

    /// <summary>Assembles the unsigned <see cref="AuditRecord"/> from its consolidated parts (doc 05 §4 step 4).</summary>
    internal static AuditRecord BuildAuditRecord(
        ConsolidateAuditInput input,
        string engagementId,
        string workflowId,
        ExecutionSnapshot snapshot,
        IReadOnlyList<AuditTelemetryRecord> telemetry) => new()
    {
        ExecutionId = input.ExecutionId,
        EngagementId = engagementId,
        WorkflowId = workflowId,
        DefinitionVersion = snapshot.DefinitionVersion,
        DefinitionHash = input.DefinitionHash,
        StartedAtUtc = input.StartedAtUtc,
        ClosedAtUtc = snapshot.CheckpointedAtUtc,
        FinalStatus = snapshot.Status,
        OrchestrationEvents = WorkflowEventProjector.Project(snapshot, input.StartedAtUtc),
        AgentInvocations = AgentInvocationProjector.Project(telemetry),
        ValidatorOutcomes = [],
        HumanDecisions = HumanDecisionProjector.Project(snapshot.Decisions),
        CacheMetrics = CacheMetricsAggregator.Aggregate(telemetry),
        // S9.38e: SANDBOX-{guid} engagement ids are minted only by S9.38a's TestRunExecutorAdapter.
        Sandbox = engagementId.StartsWith(SandboxEngagementIdPrefix, StringComparison.Ordinal) ? true : null,
    };
}
