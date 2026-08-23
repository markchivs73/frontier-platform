namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The <c>InvokeAgentActivity</c> pipeline (doc 00 §4.3 step 5, S4.2). Public so
/// <see cref="AgentTaskActivity"/> — a <c>[DurableTask]</c>-registered <c>TaskActivity</c>
/// DI must construct via a public constructor — can take it as a constructor parameter,
/// following the same pattern as <c>ICascadeEvaluator</c>/<c>ISnapshotStore</c> for their
/// activities.
/// </summary>
public interface IAgentTaskActivityPipeline
{
    /// <summary>Runs the full pipeline for <paramref name="input"/> and returns its validated output.</summary>
    Task<AgentTaskActivityResult> RunAsync(AgentTaskActivityInput input, CancellationToken ct);
}
