using Frontier.Platform.ContextAssembly;

using Frontier.Platform.Serialization;
namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary><see cref="IContextAssembler"/> double that records its inputs and returns a fixed package.</summary>
internal sealed class FakeContextAssembler(ContextPackage result) : IContextAssembler
{
    public CachingMetadata? ReceivedMetadata { get; private set; }

    public string? ReceivedBaselineContent { get; private set; }

    public string? ReceivedDynamicContent { get; private set; }

    public string? ReceivedRealTimeContent { get; private set; }
    public DynamicTierProvenance? ReceivedDynamicProvenance { get; private set; }

    public Task<ContextPackage> AssembleAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        DynamicTierProvenance? dynamicProvenance = null,
        CancellationToken ct = default)
    {
        ReceivedDynamicProvenance = dynamicProvenance;
        ReceivedMetadata = metadata;
        ReceivedBaselineContent = baselineContent;
        ReceivedDynamicContent = dynamicContent;
        ReceivedRealTimeContent = realTimeContent;
        return Task.FromResult(result);
    }
}
