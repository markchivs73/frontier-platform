
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
    Task<ValidationReport> ValidateAsync(WorkflowDefinition draft, string draftRevision, CancellationToken ct);

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
