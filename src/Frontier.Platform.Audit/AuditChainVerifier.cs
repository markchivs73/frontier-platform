
namespace Frontier.Platform.Audit;

/// <summary>
/// Re-verification logic for an <c>audit-records</c> hash chain (doc 05 §5, §2).
///
/// <para>
/// Verification follows the <em>hash links</em> from genesis (ADR-PA31, Mark's decision
/// 2026-09-16): a hash chain's order **is** its append order, and each record names its predecessor.
/// It does not iterate the stored <c>closed_at_utc</c> order, which is a business field that was
/// only incidentally aligned with append order before the concurrency guard existed. Under the
/// guard, the order in which closes win the head race is independent of their timestamps, so a
/// perfectly intact chain is routinely stored "out of order" — walking the stored order would report
/// that as a break, which is the false-tamper failure this package exists to prevent. Reads are
/// still returned in <c>closed_at_utc</c> order; only verification changed.
/// </para>
/// </summary>
internal static class AuditChainVerifier
{
    /// <summary>
    /// Builds the <see cref="VerificationResult"/> for <paramref name="executionId"/>'s record
    /// within <paramref name="chain"/> (doc 05 §2 <c>IAuditSigner.VerifyAsync</c>).
    /// </summary>
    internal static VerificationResult Verify(
        IReadOnlyList<SignedAuditRecord> chain,
        string executionId,
        string engagementId,
        IReadOnlyDictionary<string, SigningKey> keys,
        AuditChainHead? head = null)
    {
        var target = chain.FirstOrDefault(record => record.ExecutionId == executionId)
            ?? throw new InvalidOperationException($"No audit record found for '{executionId}' (doc 05 §6 expects one '{{executionId}}:audit' document per execution).");

        var walk = WalkFromGenesis(chain, engagementId, keys);
        var unreachable = FindUnreachable(chain, walk.Visited);
        var forks = FindForks(chain, head);
        var brokenLinkAt = walk.TamperedAt ?? (unreachable is null ? null : unreachable[0]);
        var headMismatch = forks is null && unreachable is null ? InspectHead(walk.Path, head) : null;
        var unresolved = FindUnresolvedKeyIds(chain, keys);
        var verdict = VerdictOf(brokenLinkAt is not null || forks is not null || headMismatch is not null, unresolved);

        return new VerificationResult
        {
            SignatureValid = IsSignatureValid(target, keys),
            ChainValid = verdict == AuditChainVerdict.Valid,
            BrokenLinkAt = brokenLinkAt,
            VerifiedAgainstKeyId = target.SigningKeyId,
            UnresolvedKeyIds = unresolved,
            Forks = forks,
            UnreachableRecords = unreachable,
            Verdict = verdict,
            HeadMismatch = headMismatch,
        };
    }

    /// <summary>
    /// The three-way verdict (S13.334 B1, ADR-PA22 C revised): any evidence of damage is
    /// <see cref="AuditChainVerdict.Broken"/>; otherwise any unresolvable key id anywhere in the chain is
    /// <see cref="AuditChainVerdict.Unverified"/>, because those records' signatures were never checked.
    /// </summary>
    internal static AuditChainVerdict VerdictOf(bool damaged, IReadOnlyList<string>? unresolvedKeyIds) =>
        damaged ? AuditChainVerdict.Broken
        : unresolvedKeyIds is not null ? AuditChainVerdict.Unverified
        : AuditChainVerdict.Valid;

    /// <summary>
    /// Whether the chain head is on the walk where it says it is (S13.334 B2): the record at position
    /// <see cref="AuditChainHead.Sequence"/> must carry the head's record hash. The walk may be longer, because the
    /// head is read before the chain and a record appended in between extends it. Null when the head agrees or there
    /// is none. The caller skips this when a fork or unreachable record exists, since those leave records off the walk.
    /// </summary>
    internal static AuditChainHeadMismatch? InspectHead(IReadOnlyList<SignedAuditRecord> path, AuditChainHead? head)
    {
        if (head is null) return null;

        var atHead = head.Sequence >= 1 && head.Sequence <= path.Count ? path[(int)head.Sequence - 1] : null;
        if (atHead is not null && string.Equals(atHead.RecordHash, head.RecordHash, StringComparison.Ordinal)) return null;

        return new AuditChainHeadMismatch
        {
            HeadSequence = head.Sequence,
            HeadRecordHash = head.RecordHash,
            WalkedCount = path.Count,
            WalkedRecordHash = (atHead ?? (path.Count > 0 ? path[^1] : null))?.RecordHash,
        };
    }

    /// <summary>
    /// Whether <paramref name="record"/>'s stored <c>RecordHash</c> matches a recomputation over its content and
    /// predecessor (S13.334 item 1(a)). Needs no key, so it runs on every walked record: an edit that left the stored
    /// hash alone is broken even when the record's key version cannot be resolved.
    /// </summary>
    internal static bool HashMatches(SignedAuditRecord record) =>
        AuditRecordHasher.ComputeRecordHash(AuditRecordHasher.ToAuditRecord(record), record.PreviousRecordHash) == record.RecordHash;

    /// <summary>
    /// Whether <paramref name="record"/>'s <c>RecordHash</c> and <c>Signature</c> match a
    /// recomputation against the key version <paramref name="record"/> records in its
    /// <see cref="SignedAuditRecord.SigningKeyId"/> — never against "the current key" (doc 05 §5).
    /// An id absent from <paramref name="keys"/> fails closed.
    /// </summary>
    internal static bool IsSignatureValid(SignedAuditRecord record, IReadOnlyDictionary<string, SigningKey> keys)
    {
        if (!keys.TryGetValue(record.SigningKeyId, out var key))
        {
            return false;
        }

        var auditRecord = AuditRecordHasher.ToAuditRecord(record);
        var expectedRecordHash = AuditRecordHasher.ComputeRecordHash(auditRecord, record.PreviousRecordHash);

        // The algorithm comes from the resolved key version, not the record (ADR-PA33): a record's
        // signing_key_id names exactly one version, and that version has exactly one algorithm.
        return expectedRecordHash == record.RecordHash &&
            AuditSignatureVerifier.Verify(expectedRecordHash, record.Signature, key);
    }

    /// <summary>
    /// The distinct <see cref="SignedAuditRecord.SigningKeyId"/> values in <paramref name="chain"/>
    /// that <paramref name="keys"/> cannot resolve, in chain order, or <see langword="null"/> when
    /// every id resolved. Reported chain-wide, not just for the verified record: a caller auditing
    /// a chain needs to know a key version is gone even when its own record verifies.
    /// </summary>
    internal static IReadOnlyList<string>? FindUnresolvedKeyIds(IReadOnlyList<SignedAuditRecord> chain, IReadOnlyDictionary<string, SigningKey> keys)
    {
        string[] unresolved =
        [
            .. chain.Select(record => record.SigningKeyId)
                .Distinct(StringComparer.Ordinal)
                .Where(keyId => !keys.ContainsKey(keyId)),
        ];

        return unresolved.Length > 0 ? unresolved : null;
    }

    /// <summary>
    /// Where verification stopped trusting the chain: the first record on the walk whose signature
    /// fails against an available key, or failing that the first record the links cannot reach.
    /// <see langword="null"/> when the chain is whole. Retained as the single-answer form of
    /// <see cref="Verify"/>'s walk.
    /// </summary>
    internal static string? FindBrokenLink(IReadOnlyList<SignedAuditRecord> chain, string engagementId, IReadOnlyDictionary<string, SigningKey> keys)
    {
        var walk = WalkFromGenesis(chain, engagementId, keys);
        var unreachable = FindUnreachable(chain, walk.Visited);

        return walk.TamperedAt ?? (unreachable is null ? null : unreachable[0]);
    }

    /// <summary>
    /// Follows <paramref name="chain"/>'s hash links from the engagement's genesis, recording which
    /// records lie on the chain and the first whose signature fails against a key that <em>is</em>
    /// available, or whose recomputed hash does not match its stored one (S13.334 item 1(a), checked on every
    /// record). An unresolvable key version is not itself a break: the walk continues and the version is
    /// reported through <see cref="VerificationResult.UnresolvedKeyIds"/>, which makes the verdict unverified
    /// (ADR-PA22 C revised). A record that links back to one already walked ends the walk rather than looping.
    /// </summary>
    internal static ChainWalk WalkFromGenesis(IReadOnlyList<SignedAuditRecord> chain, string engagementId, IReadOnlyDictionary<string, SigningKey> keys)
    {
        var byPredecessor = BuildPredecessorIndex(chain);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<SignedAuditRecord>();
        var cursor = AuditRecordHasher.ComputeGenesisHash(engagementId);
        string? tamperedAt = null;

        while (byPredecessor.TryGetValue(cursor, out var next) && visited.Add(next.ExecutionId))
        {
            path.Add(next);
            if (tamperedAt is null && IsTampered(next, keys)) tamperedAt = next.ExecutionId;
            cursor = next.RecordHash;
        }

        return new ChainWalk(visited, tamperedAt, path);
    }

    /// <summary>A recomputed-hash mismatch, or a signature that fails against a key version that is available.</summary>
    internal static bool IsTampered(SignedAuditRecord record, IReadOnlyDictionary<string, SigningKey> keys) =>
        !HashMatches(record) || (keys.ContainsKey(record.SigningKeyId) && !IsSignatureValid(record, keys));

    /// <summary>
    /// Indexes <paramref name="chain"/> by each record's predecessor hash. Where a fork puts two
    /// records on one predecessor the first in stored order wins, so the walk is deterministic; the
    /// arms it does not take surface as unreachable records beside the fork itself.
    /// </summary>
    internal static Dictionary<string, SignedAuditRecord> BuildPredecessorIndex(IReadOnlyList<SignedAuditRecord> chain)
    {
        var index = new Dictionary<string, SignedAuditRecord>(StringComparer.Ordinal);

        foreach (var record in chain)
        {
            index.TryAdd(record.PreviousRecordHash, record);
        }

        return index;
    }

    /// <summary>
    /// The execution ids in <paramref name="chain"/> the walk never reached, in stored order, or
    /// <see langword="null"/> when every record lies on the chain. A stored record that the links
    /// cannot reach is evidence that something is missing, altered or branched — reported on its own
    /// terms, never as a signature mismatch.
    /// </summary>
    internal static IReadOnlyList<string>? FindUnreachable(IReadOnlyList<SignedAuditRecord> chain, IReadOnlySet<string> visited)
    {
        string[] unreachable = [.. chain.Where(record => !visited.Contains(record.ExecutionId)).Select(record => record.ExecutionId)];

        return unreachable.Length > 0 ? unreachable : null;
    }

    /// <summary>
    /// Every place two or more records claim the same <see cref="SignedAuditRecord.PreviousRecordHash"/>,
    /// in chain order, or <see langword="null"/> when the chain is linear (ADR-PA31).
    /// </summary>
    internal static IReadOnlyList<AuditChainFork>? FindForks(IReadOnlyList<SignedAuditRecord> chain, AuditChainHead? head)
    {
        AuditChainFork[] forks =
        [
            .. chain.Select((record, index) => (record, position: index + 1))
                .GroupBy(entry => entry.record.PreviousRecordHash, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => new AuditChainFork
                {
                    PreviousRecordHash = group.Key,
                    ExecutionIds = [.. group.Select(entry => entry.record.ExecutionId)],
                    Kind = ClassifyFork(group.Select(entry => entry.position), head),
                }),
        ];

        return forks.Length > 0 ? forks : null;
    }

    /// <summary>
    /// Whether a fork at <paramref name="positions"/> predates the concurrency guard (ADR-PA31).
    ///
    /// <para>
    /// The signed record carries no "written under the guard" marker and ADR-PA31 deliberately did
    /// not add one, so this is an inference from the engagement's chain head, and it is worth being
    /// plain about its limits. No head at all means nothing in this chain has been appended to since
    /// the upgrade, so every record predates the guard. Where a head exists, the records that already
    /// existed when it was created are the unguarded ones, and the head froze that count. A fork
    /// lying wholly inside that prefix could not have been created under the guard; one reaching
    /// beyond it could not have been created by this platform at all, and is reported as
    /// <see cref="AuditChainForkKind.Guarded"/>. The head is mutable bookkeeping rather than signed
    /// evidence, so someone with write access to it could inflate the count and have a real fork
    /// mislabelled — which is why a fork of either kind still makes the chain invalid. The kind
    /// explains a finding; it never clears one.
    /// </para>
    /// </summary>
    internal static AuditChainForkKind ClassifyFork(IEnumerable<int> positions, AuditChainHead? head) =>
        head is null || positions.Max() <= head.UnguardedRecordCount
            ? AuditChainForkKind.Legacy
            : AuditChainForkKind.Guarded;
}

/// <summary>
/// What following the hash links from genesis found (ADR-PA31): the execution ids lying on the
/// chain, the first record on it whose hash or signature failed, and the records in walk order.
/// </summary>
internal sealed record ChainWalk(IReadOnlySet<string> Visited, string? TamperedAt, IReadOnlyList<SignedAuditRecord> Path);
