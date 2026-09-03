using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Answers what a definition's entry node needs before it can run — the question a caller must
/// settle *before* scheduling, not discover at node 1.
/// <para>
/// It exists because entry-node detection is control-graph knowledge (<c>ControlGraphWalker</c>) that
/// lives here and is <c>internal</c>. A consumer needing "which node runs first, and what does it
/// require" would otherwise re-derive the walk, which is how one rule ends up written in six places
/// (the defect ADR-PA11 was written about).
/// </para>
/// </summary>
public interface IWorkflowEntryInspector
{
    /// <summary>
    /// The definition's entry node and its requirements, or <see langword="null"/> when the control
    /// graph has no single resolvable <see cref="AgentTaskNode"/> entry — the same condition under
    /// which <see cref="ITestRunInputSchemaProvider"/> declines, and for the same reason: a
    /// definition with several entry candidates has no one node whose needs can be stated.
    /// </summary>
    WorkflowEntry? GetEntry(WorkflowDefinition definition);
}

/// <summary>What runs first, and what it needs.</summary>
/// <param name="NodeId">The entry node's id.</param>
/// <param name="InputContractType">The contract the entry node is invoked with, once assembled.</param>
/// <param name="RequiredDynamicFields">
/// The dynamic context fields the entry node's <see cref="ContextRequest"/> declares.
/// <para>
/// <b>These are not the caller's payload.</b> Context assembly supplies them, from whatever the
/// engagement's dynamic context holds — a caller "supplies input" only in the sense of writing that
/// context first. A workflow declaring fields here can still start with no input at all, provided
/// the engagement already holds them; that is exactly how the seeded PoC engagement runs.
/// </para>
/// </param>
public sealed record WorkflowEntry(string NodeId, string InputContractType, IReadOnlyList<string> RequiredDynamicFields);
