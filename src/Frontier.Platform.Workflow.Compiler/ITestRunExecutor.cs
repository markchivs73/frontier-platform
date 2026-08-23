using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Consumer-owned seam over the real <c>GraphOrchestrator</c> execution machinery (doc 12 §5)
/// for the sandbox test-run channel (doc 13 §5, S9.38a). <c>DefinitionCompiler</c> cannot
/// reference <c>Integration.Host</c>'s <c>IOrchestrationFactory</c> directly (library-boundaries
/// skill) — the implementation adapts it and is wired only in the composition root, the S9.30
/// <c>ICascadeGraphChecker</c> pattern.
/// </summary>
public interface ITestRunExecutor
{
    /// <summary>
    /// Starts a real <c>GraphOrchestrator</c> instance for <paramref name="definition"/>, which
    /// rides inline as orchestration input exactly as ADR-2 already supports for unpublished
    /// definitions. Returns the minted <c>{engagementId}::{workflowId}</c> instance id.
    /// </summary>
    Task<string> StartAsync(string engagementId, WorkflowDefinition definition, CancellationToken ct);

    /// <summary>The latest read-optimised checkpoint for the execution, or <c>null</c> before its first checkpoint.</summary>
    Task<ExecutionSnapshot?> GetSnapshotAsync(string executionId, string engagementId, CancellationToken ct);

    /// <summary>
    /// Raises a gate decision (doc 13 §5 <c>gateMode: auto-approve</c> or the S9.38d
    /// interactive designer-decides path) by raising the same external event a real
    /// approver's decision would raise.
    /// </summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "Mirrors IOrchestrationFactory.RaiseEventAsync's established naming (doc 12 §5).")]
    Task RaiseGateDecisionAsync(string executionId, string gateId, string approverId, DecisionKind decision, string? notes, CancellationToken ct);
}
