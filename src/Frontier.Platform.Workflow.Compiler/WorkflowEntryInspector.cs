using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <inheritdoc cref="IWorkflowEntryInspector" />
internal sealed class WorkflowEntryInspector : IWorkflowEntryInspector
{
    /// <inheritdoc />
    public WorkflowEntry? GetEntry(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var entryIds = ControlGraphWalker.FindEntryNodeIds(definition);
        if (entryIds.Count != 1) return null;

        if (definition.Nodes.FirstOrDefault(n => n.NodeId == entryIds[0]) is not AgentTaskNode entry) return null;

        return new WorkflowEntry(entry.NodeId, entry.InputContractType, entry.ContextRequest.DynamicFields);
    }
}
