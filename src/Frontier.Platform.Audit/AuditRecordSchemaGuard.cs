using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// Refuses to hand back an audit record written under an incompatible schema major.
///
/// This exists because of how the two halves of this package interact. Verification
/// (<see cref="AuditChainVerifier"/>) does not hash the stored bytes — it rehydrates the
/// record and *recomputes* the canonical bytes. So a record written before a wire-name change
/// deserializes happily, with the renamed field arriving as <see langword="null"/>, and then
/// fails its signature check. The operator sees a broken hash chain, which is the signal this
/// system reserves for **tampering**.
///
/// A schema change must therefore never be allowed to look like tampering. Reading an
/// incompatible record fails loudly and specifically instead, naming the version it found.
/// </summary>
internal static class AuditRecordSchemaGuard
{
    /// <summary>The schema major this package reads. Bumped at the artifact-vocabulary rename.</summary>
    internal const string CurrentSchemaVersion = "2.0";

    /// <summary>
    /// Returns <paramref name="record"/> when it is readable, and throws
    /// <see cref="ContractViolationException"/> — a permanent failure, never retried — when it
    /// was written under a different schema major.
    /// </summary>
    internal static SignedAuditRecord EnsureReadable(SignedAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (MajorOf(record.SchemaVersion) == MajorOf(CurrentSchemaVersion))
        {
            return record;
        }

        throw new ContractViolationException(
            $"Audit record '{record.ExecutionId}' has schema_version '{record.SchemaVersion}', which this build cannot read "
            + $"(expected major {MajorOf(CurrentSchemaVersion)}). Its signature would fail recomputation and be indistinguishable "
            + "from tampering, so it is refused rather than returned. Records written before the artifact-vocabulary rename are "
            + "not migrated — see ADR-PA4.");
    }

    /// <summary>The major component of a <c>schema_version</c>, or the whole string when it carries no minor.</summary>
    internal static string MajorOf(string schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        var separator = schemaVersion.IndexOf('.', StringComparison.Ordinal);
        return separator < 0 ? schemaVersion : schemaVersion[..separator];
    }
}
