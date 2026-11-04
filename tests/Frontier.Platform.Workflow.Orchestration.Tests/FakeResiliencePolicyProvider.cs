using Frontier.Platform.Resilience;
using Microsoft.DurableTask;
using Polly;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// No-op <see cref="IResiliencePolicyProvider"/> for S2.2/S4.4 orchestrator tests:
/// <see cref="FakeTaskOrchestrationContext.CallActivityAsync{TResult}"/> ignores the
/// <see cref="TaskOptions"/> it's given, so these tests only need a value that
/// satisfies <see cref="GraphOrchestrator"/>'s constructor, not real retry behaviour.
/// Endpoint pipelines are <paramref name="endpointPipeline"/> (default <see cref="ResiliencePipeline.Empty"/>),
/// and each request is recorded, so dispatcher tests can see what a model call ran through (S13.217).
/// </summary>
internal sealed class FakeResiliencePolicyProvider(ResiliencePipeline? endpointPipeline = null) : IResiliencePolicyProvider
{
    /// <summary>The <c>profileName</c> passed to every <see cref="GetTaskOptions"/> call, in order.</summary>
    public List<string> RequestedProfileNames { get; } = [];

    /// <summary>Every endpoint pipeline request, in order.</summary>
    public List<(string Profile, string Provider, string ModelId)> RequestedEndpoints { get; } = [];

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string profileName) => ResiliencePipeline.Empty;

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string profileName, string provider, string modelId)
    {
        RequestedEndpoints.Add((profileName, provider, modelId));
        return endpointPipeline ?? ResiliencePipeline.Empty;
    }

    /// <inheritdoc />
    public TaskOptions GetTaskOptions(string profileName)
    {
        RequestedProfileNames.Add(profileName);
        return new();
    }
}
