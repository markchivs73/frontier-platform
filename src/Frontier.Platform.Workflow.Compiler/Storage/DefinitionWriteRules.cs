using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// The lost-update decisions of <see cref="CosmosDefinitionStore"/>'s draft save and version publish (doc 13 §7,
/// ADR-CD2, GOV-03), kept apart from the coverage-excluded adapter (S13.193) so the unit gate measures them: when
/// a save is stale, when a publish that found its version id taken is an idempotent re-run, and what an
/// approve-and-publish batch's outcome means (S13.248).
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

    /// <summary>An approval may be written only for a proposal that still exists and is still in review.</summary>
    internal static bool CanApprove([NotNullWhen(true)] PublishProposalDocument? stored) =>
        stored is not null && stored.State.CanTransitionTo(ProposalState.Approved);

    /// <summary>
    /// What an approve-and-publish batch's status means: <see langword="true"/> when it landed;
    /// <see langword="false"/> when another writer won, either deciding the proposal first (412) or
    /// publishing the same version number first (409), so nothing was written; and an
    /// <see cref="InvalidOperationException"/> naming <paramref name="describeFailure"/> for any other
    /// failure, which also wrote nothing but is not a lost race.
    /// </summary>
    internal static bool ApprovalLanded(HttpStatusCode status, Func<string> describeFailure)
    {
        ArgumentNullException.ThrowIfNull(describeFailure);
        if ((int)status is >= 200 and <= 299)
            return true;

        return status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict
            ? false
            : throw new InvalidOperationException(describeFailure());
    }
}
