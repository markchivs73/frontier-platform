using Frontier.Platform.Resilience;
using Microsoft.DurableTask;
using Polly;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// No-op <see cref="IResiliencePolicyProvider"/> for S2.2/S4.4 orchestrator tests:
/// <see cref="FakeTaskOrchestrationContext.CallActivityAsync{TResult}"/> ignores the
/// <see cref="TaskOptions"/> it's given, so these tests only need a value that
/// satisfies <see cref="GraphOrchestrator"/>'s constructor, not real retry behaviour.
/// </summary>
internal sealed class FakeResiliencePolicyProvider : IResiliencePolicyProvider
{
    /// <summary>The <c>profileName</c> passed to every <see cref="GetTaskOptions"/> call, in order.</summary>
    public List<string> RequestedProfileNames { get; } = [];

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string profileName) => ResiliencePipeline.Empty;

    /// <inheritdoc />
    public TaskOptions GetTaskOptions(string profileName)
    {
        RequestedProfileNames.Add(profileName);
        return new();
    }
}
