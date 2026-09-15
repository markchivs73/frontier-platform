namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// In-memory <see cref="IGovernanceAuditStore"/> with the Cosmos batch's semantics (ADR-PA30): the
/// append is atomic and conditional on the head's ETag, and a taken sequence or record id is a
/// conflict. <see cref="BeforeAppend"/> lets a test move the head between the read and the write,
/// which is exactly the 412 a concurrent writer causes.
/// </summary>
internal sealed class FakeGovernanceAuditStore : IGovernanceAuditStore
{
    private readonly Lock gate = new();
    private readonly List<SignedGovernanceAuditRecord> records = [];
    private GovernanceAuditHeadState? head;
    private int etagCounter;
    private int appendCalls;
    private int conflicts;
    private int readCalls;

    /// <summary>Runs inside <see cref="TryAppendAsync"/> before the conditional write.</summary>
    internal Func<Task>? BeforeAppend { get; set; }

    /// <summary>Makes every append a conflict.</summary>
    internal bool AlwaysConflict { get; set; }

    /// <summary>Yields between the read and the write, so parallel appends genuinely interleave.</summary>
    internal bool YieldBeforeWrite { get; set; }

    internal int AppendCalls => appendCalls;

    internal int ConflictsReturned => conflicts;

    internal int ReadCalls => readCalls;

    internal IReadOnlyList<SignedGovernanceAuditRecord> Records
    {
        get
        {
            lock (gate)
            {
                return [.. records];
            }
        }
    }

    public Task<GovernanceAuditHeadState?> ReadHeadAsync(string scope, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref readCalls);
        lock (gate)
        {
            return Task.FromResult(head?.Head.Scope == scope ? head : null);
        }
    }

    public Task<SignedGovernanceAuditRecord?> FindInScopeAsync(string scope, string recordId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref readCalls);
        lock (gate)
        {
            return Task.FromResult(records.FirstOrDefault(record => record.Scope == scope && record.RecordId == recordId));
        }
    }

    public async Task<bool> TryAppendAsync(SignedGovernanceAuditRecord record, string? expectedHeadETag, CancellationToken cancellationToken)
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
            if (AlwaysConflict || head?.ETag != expectedHeadETag || records.Any(stored => stored.Scope == record.Scope && (stored.RecordId == record.RecordId || stored.Sequence == record.Sequence)))
            {
                conflicts++;
                return false;
            }

            records.Add(record);
            head = new GovernanceAuditHeadState(
                new GovernanceAuditChainHead { Scope = record.Scope, Sequence = record.Sequence, RecordHash = record.RecordHash },
                $"etag-{++etagCounter}");
            return true;
        }
    }

    public Task<SignedGovernanceAuditRecord?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(records.FirstOrDefault(record => record.RecordId == recordId));
        }
    }

    public Task<IReadOnlyList<SignedGovernanceAuditRecord>> GetChainAsync(string scope, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<SignedGovernanceAuditRecord>>([.. records.Where(record => record.Scope == scope).OrderBy(record => record.Sequence)]);
        }
    }

    public Task<GovernanceAuditPage> QueryAsync(GovernanceAuditQuery query, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var matching = records
                .Where(record => record.Scope == query.Scope)
                .Where(record => query.SubjectType is null || record.SubjectType == query.SubjectType)
                .Where(record => query.SubjectId is null || record.SubjectId == query.SubjectId)
                .Where(record => query.Actor is null || record.Actor == query.Actor)
                .Where(record => query.EventType is null || record.EventType == query.EventType)
                .Where(record => query.FromUtc is null || record.OccurredAtUtc >= query.FromUtc)
                .Where(record => query.ToUtc is null || record.OccurredAtUtc <= query.ToUtc)
                .OrderBy(record => record.Sequence)
                .ToList();

            var skip = int.Parse(query.ContinuationToken ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var next = skip + query.PageSize;
            return Task.FromResult(new GovernanceAuditPage
            {
                Records = [.. matching.Skip(skip).Take(query.PageSize)],
                ContinuationToken = next < matching.Count ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            });
        }
    }
}
