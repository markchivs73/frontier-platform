using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Deterministic <see cref="IAuditConsolidator"/> test double for <see cref="ConsolidateAuditActivityTests"/>
/// (S5.6): returns <see cref="AuditFixtures.UnsignedRecord"/> for whatever <see cref="ConsolidateAuditInput"/>
/// it receives, and records every input it was called with.
/// </summary>
internal sealed class FakeAuditConsolidator : IAuditConsolidator
{
    /// <summary>Every <see cref="ConsolidateAuditInput"/> passed to <see cref="ConsolidateAsync"/>, in call order.</summary>
    public List<ConsolidateAuditInput> Inputs { get; } = [];

    /// <inheritdoc />
    public Task<AuditRecord> ConsolidateAsync(ConsolidateAuditInput input, CancellationToken cancellationToken)
    {
        Inputs.Add(input);
        return Task.FromResult(AuditFixtures.UnsignedRecord(input));
    }
}
