using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>In-memory <see cref="IAuditTelemetryStaging"/> test double for the S5.3 pipeline tests and the relocated S5.4 consolidator tests (S11.6): optionally seeded with pre-staged records.</summary>
internal sealed class FakeAuditTelemetryStaging(IReadOnlyList<AuditTelemetryRecord>? seed = null) : IAuditTelemetryStaging
{
    private readonly List<AuditTelemetryRecord> records = [.. seed ?? []];

    /// <summary>Every <see cref="AuditTelemetryRecord"/> recorded via <see cref="RecordInvocationAsync"/>.</summary>
    internal IReadOnlyList<AuditTelemetryRecord> Records => records;

    public Task RecordInvocationAsync(AuditTelemetryRecord record, CancellationToken cancellationToken)
    {
        records.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditTelemetryRecord>> GetForExecutionAsync(string executionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditTelemetryRecord>>(records.Where(r => r.ExecutionId == executionId).ToArray());
}
