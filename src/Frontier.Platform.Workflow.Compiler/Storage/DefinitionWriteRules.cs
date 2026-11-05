using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// The lost-update decisions of <see cref="CosmosDefinitionStore"/>'s draft save and version publish (doc 13 §7,
/// ADR-CD2, GOV-03), kept apart from the coverage-excluded adapter (S13.193) so the unit gate measures them: when
/// a save is stale, and when a publish that found its version id taken is an idempotent re-run.
/// </summary>
internal static class DefinitionWriteRules
{
    /// <summary>
    /// A save is stale when a draft exists and its <see cref="DefinitionDraftDocument.DraftRevision"/> — the value the
    /// API exposes as the client ETag (ADR-CD2) — is not the one the caller edited from. A first save is never stale.
    /// </summary>
    internal static bool IsStaleSave([NotNullWhen(true)] DefinitionDraftDocument? current, string expectedRevision) =>
        current is not null && !string.Equals(current.DraftRevision, expectedRevision, StringComparison.Ordinal);

    /// <summary>The conflict a stale or lost save reports: the winner's revision and document.</summary>
    internal static SaveDraftResultConflict Conflict(DefinitionDraftDocument winner) =>
        new("unknown", winner.DraftRevision, winner);

    /// <summary>
    /// A publish whose version document already exists: an idempotent re-run when the stored definition hash is the one
    /// being published (an earlier attempt created the document but failed before moving the pointer), so the stored
    /// document is reused; otherwise the version is taken by a different definition and the publish is refused.
    /// </summary>
    internal static DefinitionVersionDocument ResolvePublishConflict(DefinitionVersionDocument? existing, DefinitionVersionDocument publishing)
    {
        ArgumentNullException.ThrowIfNull(publishing);

        return existing is not null && existing.DefinitionHash == publishing.DefinitionHash
            ? existing
            : throw new InvalidOperationException(
                $"Version {publishing.DefinitionVersion} already exists with a different definition hash — cannot publish.");
    }
}
