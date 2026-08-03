
namespace Frontier.Platform.Audit.Tests;

/// <summary>In-memory <see cref="IAuditRecordStore"/> test double for S5.5 signer/verifier tests.</summary>
internal sealed class FakeAuditRecordStore : IAuditRecordStore
{
    private readonly List<SignedAuditRecord> records = [];

    /// <inheritdoc />
    public Task<SignedAuditRecord?> GetAsync(string executionId, CancellationToken cancellationToken) =>
        Task.FromResult(records.FirstOrDefault(record => record.ExecutionId == executionId));

    /// <inheritdoc />
    public Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SignedAuditRecord>>(records
            .Where(record => record.EngagementId == engagementId)
            .OrderBy(record => record.ClosedAtUtc)
            .ToArray());

    /// <inheritdoc />
    public Task CreateAsync(SignedAuditRecord record, CancellationToken cancellationToken)
    {
        if (records.Any(existing => existing.ExecutionId == record.ExecutionId))
        {
            throw new InvalidOperationException($"An audit record for '{record.ExecutionId}' already exists (audit-records is append-only, doc 05 §6).");
        }

        records.Add(record);
        return Task.CompletedTask;
    }

    /// <summary>Overwrites the stored record matching <paramref name="record"/>'s execution id — simulates a tampered stored copy for verify tests.</summary>
    internal void Replace(SignedAuditRecord record)
    {
        records.RemoveAll(existing => existing.ExecutionId == record.ExecutionId);
        records.Add(record);
    }
}
