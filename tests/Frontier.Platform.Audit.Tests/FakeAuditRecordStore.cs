
namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// In-memory <see cref="IAuditRecordStore"/> + <see cref="IAuditChainHeadStore"/> test double for the
/// S5.5 signer/verifier tests, extended at S13.106 with the Cosmos batch's semantics (ADR-PA31): the
/// append is atomic and conditional on the head's ETag, and a taken execution id is a conflict.
/// <see cref="BeforeAppend"/> lets a test move the head between the read and the write, which is
/// exactly the 412 a concurrent close causes.
/// </summary>
internal sealed class FakeAuditRecordStore : IAuditRecordStore, IAuditChainHeadStore
{
    private readonly Lock gate = new();
    private readonly List<SignedAuditRecord> records = [];
    private AuditChainHeadState? head;
    private int etagCounter;
    private int appendCalls;
    private int conflicts;

    /// <summary>Runs inside <see cref="TryAppendAsync"/> before the conditional write.</summary>
    internal Func<Task>? BeforeAppend { get; set; }

    /// <summary>Makes every append a conflict.</summary>
    internal bool AlwaysConflict { get; set; }

    /// <summary>Yields between the read and the write, so parallel appends genuinely interleave.</summary>
    internal bool YieldBeforeWrite { get; set; }

    /// <summary>How many conditional appends were attempted.</summary>
    internal int AppendCalls => appendCalls;

    /// <summary>How many of those lost the head and were told to re-read.</summary>
    internal int ConflictsReturned => conflicts;

    /// <summary>The engagement's head, or <see langword="null"/> when none has been created.</summary>
    internal AuditChainHead? Head
    {
        get
        {
            lock (gate)
            {
                return head?.Head;
            }
        }
    }

    /// <inheritdoc />
    public Task<SignedAuditRecord?> GetAsync(string executionId, string engagementId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(records.FirstOrDefault(record => record.ExecutionId == executionId && record.EngagementId == engagementId));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<SignedAuditRecord>>([.. records
                .Where(record => record.EngagementId == engagementId)
                .OrderBy(record => record.ClosedAtUtc)]);
        }
    }

    /// <inheritdoc />
    public Task CreateAsync(SignedAuditRecord record, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (records.Any(existing => existing.ExecutionId == record.ExecutionId))
            {
                throw new InvalidOperationException($"An audit record for '{record.ExecutionId}' already exists (audit-records is append-only, doc 05 §6).");
            }

            records.Add(record);
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<AuditChainHeadState?> ReadHeadAsync(string engagementId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(head?.Head.EngagementId == engagementId ? head : null);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAppendAsync(SignedAuditRecord record, AuditChainHead newHead, string? expectedHeadETag, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref appendCalls);

        if (BeforeAppend is { } hook)
        {
            await hook();
        }

        if (YieldBeforeWrite)
        {
            await Task.Yield();
        }

        lock (gate)
        {
            if (AlwaysConflict || head?.ETag != expectedHeadETag || records.Any(stored => stored.ExecutionId == record.ExecutionId))
            {
                conflicts++;
                return false;
            }

            records.Add(record);
            head = new AuditChainHeadState(newHead, $"etag-{++etagCounter}");
            return true;
        }
    }

    /// <summary>
    /// Seeds a record with no head movement — a chain as it was stored before ADR-PA31, which is
    /// what the migration and legacy-fork tests need.
    /// </summary>
    internal void SeedUnguarded(SignedAuditRecord record)
    {
        lock (gate)
        {
            records.Add(record);
        }
    }

    /// <summary>Overwrites the stored record matching <paramref name="record"/>'s execution id — simulates a tampered stored copy for verify tests.</summary>
    internal void Replace(SignedAuditRecord record)
    {
        lock (gate)
        {
            records.RemoveAll(existing => existing.ExecutionId == record.ExecutionId);
            records.Add(record);
        }
    }
}
