
namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>
/// The scripted fake model behind the real <see cref="AgentTaskActivity"/> shell (S13.192): no key,
/// no cost, no network. Each node answers after its scripted <c>delays</c> entry (zero when absent)
/// with the same stable payload <see cref="FakeAgentTaskActivityPipeline"/> produces, and every call
/// is recorded with the worker it ran on, so a test can tell a replayed step from a re-run one.
/// </summary>
internal sealed class ScriptedAgentTaskActivityPipeline(
    string worker,
    ReplayRecorder recorder,
    IReadOnlyDictionary<string, TimeSpan> delays) : IAgentTaskActivityPipeline
{
    private readonly FakeAgentTaskActivityPipeline _answers = new();

    /// <inheritdoc />
    public async Task<AgentTaskActivityResult> RunAsync(AgentTaskActivityInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var startedAt = DateTimeOffset.UtcNow;
        recorder.AddStarted(input.NodeId);
        await Task.Delay(delays.GetValueOrDefault(input.NodeId, TimeSpan.Zero), ct);
        var result = await _answers.RunAsync(input, ct);
        recorder.Add(new AgentCall(input.NodeId, worker, startedAt, DateTimeOffset.UtcNow, input.DynamicContextEpoch));
        return result;
    }
}
