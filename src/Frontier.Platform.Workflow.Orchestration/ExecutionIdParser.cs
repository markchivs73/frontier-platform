namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Splits an execution id into its <c>engagementId</c>/<c>workflowId</c> parts per the
/// <c>{engagementId}::{workflowId}</c> instance-id format (rule 3). Dispatcher children
/// append a third <c>::{workItemId}</c> segment, which is ignored here — the audit
/// consolidator (S5.4) runs only against top-level executions (S5.6 wires it after the
/// root orchestration's final checkpoint).
/// </summary>
internal static class ExecutionIdParser
{
    /// <summary>Returns the leading <c>engagementId</c> and <c>workflowId</c> segments of <paramref name="executionId"/>.</summary>
    internal static (string EngagementId, string WorkflowId) Parse(string executionId)
    {
        var parts = executionId.Split("::");
        if (parts.Length < 2)
        {
            throw new ArgumentException($"Execution id '{executionId}' is not in '{{engagementId}}::{{workflowId}}' format.", nameof(executionId));
        }

        return (parts[0], parts[1]);
    }
}
