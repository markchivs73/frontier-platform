namespace Frontier.Platform.Audit;

/// <summary>
/// Pure verification of a governance chain (ADR-PA30). It never throws for a broken chain:
/// every break is listed with its position, so an auditor sees where tampering, loss or a
/// destroyed key version sits. Usable offline, for example over an archive copy.
/// </summary>
public static class GovernanceAuditChainVerifier
{
    /// <summary>
    /// Walks <paramref name="records"/> in stored order (1-based positions) from the scope's genesis,
    /// checks each record's sequence, link, scope and signature against the key version it names, and
    /// checks <paramref name="head"/> against the last record.
    /// </summary>
    public static GovernanceAuditVerificationResult Verify(
        string scope,
        IReadOnlyList<SignedGovernanceAuditRecord> records,
        GovernanceAuditChainHead? head,
        IReadOnlyDictionary<string, SigningKey> keys)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(keys);

        var breaks = WalkRecords(scope, records, keys);
        if (InspectHead(records, head) is { } headBreak)
        {
            breaks.Add(headBreak);
        }

        return new GovernanceAuditVerificationResult
        {
            Scope = scope,
            Valid = breaks.Count == 0,
            RecordCount = records.Count,
            HeadSequence = head?.Sequence,
            Breaks = breaks.Count > 0 ? breaks : null,
            UnresolvedKeyIds = FindUnresolvedKeyIds(records, keys),
        };
    }

    /// <summary>Every break found walking <paramref name="records"/> from genesis.</summary>
    internal static List<GovernanceAuditChainBreak> WalkRecords(string scope, IReadOnlyList<SignedGovernanceAuditRecord> records, IReadOnlyDictionary<string, SigningKey> keys)
    {
        var breaks = new List<GovernanceAuditChainBreak>();
        var expectedPrevious = GovernanceAuditHasher.ComputeGenesisHash(scope);
        var expectedSequence = 1L;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            breaks.AddRange(InspectRecord(scope, record, index + 1, expectedSequence, expectedPrevious, keys));
            expectedSequence = record.Sequence + 1;
            expectedPrevious = record.RecordHash;
        }

        return breaks;
    }

    /// <summary>The breaks <paramref name="record"/> itself shows at <paramref name="position"/>.</summary>
    internal static IEnumerable<GovernanceAuditChainBreak> InspectRecord(
        string scope,
        SignedGovernanceAuditRecord record,
        long position,
        long expectedSequence,
        string expectedPrevious,
        IReadOnlyDictionary<string, SigningKey> keys)
    {
        if (SequenceBreak(record.Sequence, expectedSequence) is { } sequenceKind)
        {
            yield return Break(position, sequenceKind, record);
        }

        if (!string.Equals(record.PreviousRecordHash, expectedPrevious, StringComparison.Ordinal))
        {
            yield return Break(position, GovernanceAuditBreakKind.HashLinkBreak, record);
        }

        if (!string.Equals(record.Scope, scope, StringComparison.Ordinal))
        {
            yield return Break(position, GovernanceAuditBreakKind.ScopeMismatch, record);
        }

        if (SignatureBreak(record, keys) is { } signatureKind)
        {
            yield return Break(position, signatureKind, record);
        }
    }

    /// <summary><see cref="GovernanceAuditBreakKind.OutOfOrder"/> for a sequence behind the walk, <see cref="GovernanceAuditBreakKind.SequenceGap"/> for one ahead of it.</summary>
    internal static GovernanceAuditBreakKind? SequenceBreak(long actual, long expected) =>
        actual < expected ? GovernanceAuditBreakKind.OutOfOrder
        : actual > expected ? GovernanceAuditBreakKind.SequenceGap
        : null;

    /// <summary>
    /// <see cref="GovernanceAuditBreakKind.UnresolvedKey"/> when the record's own key version cannot be
    /// resolved (fail closed, ADR-PA22); <see cref="GovernanceAuditBreakKind.SignatureMismatch"/> when its
    /// content no longer matches its hash or its hash no longer matches its signature.
    /// </summary>
    internal static GovernanceAuditBreakKind? SignatureBreak(SignedGovernanceAuditRecord record, IReadOnlyDictionary<string, SigningKey> keys)
    {
        if (!keys.TryGetValue(record.SigningKeyId, out var key))
        {
            return GovernanceAuditBreakKind.UnresolvedKey;
        }

        var expectedHash = GovernanceAuditHasher.ComputeRecordHash(record);

        return expectedHash == record.RecordHash && AuditSignatureVerifier.Verify(expectedHash, record.Signature, key)
            ? null
            : GovernanceAuditBreakKind.SignatureMismatch;
    }

    /// <summary>A <see cref="GovernanceAuditBreakKind.HeadMismatch"/> when the head does not name the last stored record, else <see langword="null"/>.</summary>
    internal static GovernanceAuditChainBreak? InspectHead(IReadOnlyList<SignedGovernanceAuditRecord> records, GovernanceAuditChainHead? head)
    {
        var last = records.Count > 0 ? records[^1] : null;
        var agrees = last is null
            ? head is null
            : head is not null && head.Sequence == last.Sequence && string.Equals(head.RecordHash, last.RecordHash, StringComparison.Ordinal);

        return agrees
            ? null
            : new GovernanceAuditChainBreak { Position = records.Count, Kind = GovernanceAuditBreakKind.HeadMismatch, RecordedSequence = head?.Sequence };
    }

    /// <summary>The distinct key ids in <paramref name="records"/> that <paramref name="keys"/> cannot resolve, in chain order, or <see langword="null"/>.</summary>
    internal static IReadOnlyList<string>? FindUnresolvedKeyIds(IReadOnlyList<SignedGovernanceAuditRecord> records, IReadOnlyDictionary<string, SigningKey> keys)
    {
        string[] unresolved = [.. records.Select(record => record.SigningKeyId).Distinct(StringComparer.Ordinal).Where(keyId => !keys.ContainsKey(keyId))];
        return unresolved.Length > 0 ? unresolved : null;
    }

    private static GovernanceAuditChainBreak Break(long position, GovernanceAuditBreakKind kind, SignedGovernanceAuditRecord record) => new()
    {
        Position = position,
        Kind = kind,
        RecordId = record.RecordId,
        RecordedSequence = record.Sequence,
    };
}
