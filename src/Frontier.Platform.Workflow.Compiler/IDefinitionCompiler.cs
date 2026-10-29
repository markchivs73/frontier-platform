
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Compiles a workflow definition: validates, canonicalises, and computes its hash.
/// Doc 13 §6: the service contract for validation and hashing.
/// </summary>
public interface IDefinitionCompiler
{
    /// <summary>
    /// Full validation (pure + resourced tiers). Used by Validate button, publish proposal, and CI/CD.
    /// This is the authority: only reports passing this can be published.
    /// </summary>
    /// <param name="draft">The definition to validate.</param>
    /// <param name="draftRevision">The revision the report is for.</param>
    /// <param name="storedSchemaVersion">
    /// The schema version the definition was stored at, as the store probed it
    /// (<c>DefinitionDraftDocument.StoredSchemaVersion</c>), so <c>schema.version-supported</c> can
    /// say when a draft predates or outruns this build (S13.34/S13.77). <see langword="null"/> means
    /// "not read from a stored document", which is right for an in-memory definition such as the
    /// designer's proposal, a merge result or a request body. Required, not optional, so every
    /// caller states which case it is.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    Task<ValidationReport> ValidateAsync(WorkflowDefinition draft, string draftRevision, string? storedSchemaVersion, CancellationToken ct);

    /// <summary>
    /// Pure tier only—synchronous, allocation-light. The canvas calls this per debounced edit
    /// for instant feedback (red node/edge/field highlights). The returned findings are not
    /// publishable authority; full ValidateAsync is required before publish.
    /// </summary>
    IReadOnlyList<ValidationFinding> ValidateStructural(WorkflowDefinition draft);

    /// <summary>
    /// Canonicalise the definition and compute its DefinitionHash (SHA256 over canonical bytes,
    /// excluding the hash field itself). Called at publish time (doc 13 §3, ADR-DC3).
    /// </summary>
    string ComputeDefinitionHash(WorkflowDefinition definition);
}
