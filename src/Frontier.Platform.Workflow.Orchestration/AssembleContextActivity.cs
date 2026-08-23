using Frontier.Platform.ContextAssembly;
using ContextPackageContract = Frontier.Platform.Serialization.ContextPackage;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// DTF activity (S3.3 ADR-CR1): assembles a three-tier context package (baseline → dynamic → real-time)
/// for a given engagement and orchestration instance. Applies caching strategy hints via
/// <see cref="IContextAssembler"/> based on the provider and model.
///
/// Called by agents or the orchestrator before model invocation. Input is a correlated
/// <see cref="AssembleContextRequest"/>; output is a <see cref="ContextPackageContract"/>.
/// </summary>
internal sealed class AssembleContextActivity
{
    private readonly IContextAssembler assembler;

    /// <summary>
    /// Constructs an activity that assembles context via <paramref name="assembler"/>.
    /// </summary>
    public AssembleContextActivity(IContextAssembler assembler)
    {
        ArgumentNullException.ThrowIfNull(assembler);
        this.assembler = assembler;
    }

    /// <summary>
    /// Assembles the three-tier context and applies provider-specific caching strategy hints.
    /// </summary>
    public async Task<ContextPackageContract> RunAsync(AssembleContextRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        // Assemble the three tiers (baseline, dynamic, real-time) and apply caching directives.
        var package = await assembler.AssembleAsync(
            metadata: request.CachingMetadata,
            baselineContent: request.BaselineContent,
            dynamicContent: request.DynamicContent,
            realTimeContent: request.RealTimeContent,
            ct);

        return package;
    }
}
