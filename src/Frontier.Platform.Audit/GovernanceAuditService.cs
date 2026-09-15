using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IGovernanceAuditService"/> (ADR-PA30). An append validates, derives the record id,
/// returns an already-stored identical record, and otherwise reads the head, allocates the next
/// sequence, signs with the governance-purpose key and stores record, marker and head in one
/// conditional batch, re-reading and re-hashing on a concurrency conflict.
/// </summary>
internal sealed class GovernanceAuditService(
    IGovernanceAuditStore store,
    ISigningKeyRing keyRing,
    IOptions<GovernanceAuditOptions> options) : IGovernanceAuditService
{
    /// <inheritdoc />
    public async Task<SignedGovernanceAuditRecord> AppendAsync(GovernanceAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();

        var recordId = GovernanceAuditHasher.ComputeRecordId(entry);
        await EnsureCompensatedRecordExistsAsync(entry, cancellationToken);

        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.AppendMaxAttempts; attempt++)
        {
            if (await TryAppendOnceAsync(entry, recordId, cancellationToken) is { } stored)
            {
                return stored;
            }

            if (attempt < retry.AppendMaxAttempts)
            {
                await Task.Delay(GovernanceAuditBackoff.DelayFor(attempt, retry, GovernanceAuditBackoff.NextJitter()), cancellationToken);
            }
        }

        throw new GovernanceAuditAppendException(
            $"Governance audit append for record '{recordId}' in scope '{entry.Scope}' kept losing the chain head to concurrent writers; the configured attempts are exhausted.");
    }

    /// <inheritdoc />
    public Task<SignedGovernanceAuditRecord?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        return store.GetAsync(recordId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GovernanceAuditPage> QueryAsync(GovernanceAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Scope, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, GovernanceAuditQuery.MaxPageSize, nameof(query));
        return store.QueryAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GovernanceAuditVerificationResult> VerifyAsync(string scope, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var records = await store.GetChainAsync(scope, cancellationToken);
        var head = await store.ReadHeadAsync(scope, cancellationToken);
        var keyIds = records.Select(record => record.SigningKeyId);
        var keys = await new SigningKeyResolver(GovernanceKeys).ResolveKeyIdsAsync(keyIds, cancellationToken);

        return GovernanceAuditChainVerifier.Verify(scope, records, head?.Head, keys);
    }

    /// <summary>One read-hash-sign-store pass: the stored record, or <see langword="null"/> when another writer moved the head first.</summary>
    internal async Task<SignedGovernanceAuditRecord?> TryAppendOnceAsync(GovernanceAuditEntry entry, string recordId, CancellationToken cancellationToken)
    {
        if (await store.FindInScopeAsync(entry.Scope, recordId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var head = await store.ReadHeadAsync(entry.Scope, cancellationToken);
        var key = await GovernanceKeys.GetCurrentKeyAsync(cancellationToken);
        var sequence = (head?.Head.Sequence ?? 0) + 1;
        var previousHash = head?.Head.RecordHash ?? GovernanceAuditHasher.ComputeGenesisHash(entry.Scope);
        var record = GovernanceAuditHasher.Seal(entry, recordId, sequence, previousHash, key);

        return await store.TryAppendAsync(record, head?.ETag, cancellationToken) ? record : null;
    }

    /// <summary>A compensating entry must name a record already in its scope; otherwise it is a permanent contract violation.</summary>
    internal async Task EnsureCompensatedRecordExistsAsync(GovernanceAuditEntry entry, CancellationToken cancellationToken)
    {
        if (entry.CompensatesRecordId is not { } target)
        {
            return;
        }

        if (await store.FindInScopeAsync(entry.Scope, target, cancellationToken) is null)
        {
            throw new ContractViolationException(nameof(GovernanceAuditEntry), [$"compensates_record_id '{target}' names no record in scope '{entry.Scope}'."]);
        }
    }

    private IKeyProvider GovernanceKeys => keyRing.GetProvider(SigningKeyPurpose.GovernanceAudit);
}
