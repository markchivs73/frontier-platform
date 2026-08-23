
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Consumer-owned seam over Cascade Logic's publish-time guardian (doc 03 §2) for the
/// <c>cascade.acyclic</c> rule (doc 13 §4.2, S9.30). The implementation adapts
/// <c>ICascadeGraphValidator.ValidateAtPublish</c> and is wired only in the composition root, so
/// the Definition Compiler stays within its library boundary (libraries reference each other only
/// via consumer-owned abstractions — the S9.27c catalog pattern; enforced by
/// <c>ProjectReferenceRulesTests</c>).
/// </summary>
public interface ICascadeGraphChecker
{
    /// <summary>
    /// Validates the definition's derived section graph. Returns violation messages —
    /// empty when the graph is acyclic with no dangling section refs.
    /// </summary>
    IReadOnlyList<string> CheckAtPublish(WorkflowDefinition definition);
}
