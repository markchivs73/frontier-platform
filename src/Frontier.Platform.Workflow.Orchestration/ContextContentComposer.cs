using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <see cref="IContextContentComposer"/> over Context Assembly's Phase 1 stores
/// (<see cref="Phase1BaselineCatalogueStore"/>, <see cref="Phase1EngagementContextStore"/>
/// — S4.2). The configured <see cref="ContextAssemblyOptions.BaselineCatalogueId"/> names
/// the one compiled-in catalogue (<see cref="Phase1ContextCatalogue.BaselineCatalogueId"/>);
/// a missing catalogue is a boot-time configuration error, not a per-node contract issue.
/// </summary>
internal sealed class ContextContentComposer : IContextContentComposer
{
    private readonly IBaselineCatalogueStore baselineStore;
    private readonly IEngagementContextStore engagementStore;
    private readonly IOptions<ContextAssemblyOptions> options;

    /// <summary>Constructs a composer over the registered Context Assembly stores and options.</summary>
    public ContextContentComposer(
        IBaselineCatalogueStore baselineStore,
        IEngagementContextStore engagementStore,
        IOptions<ContextAssemblyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(baselineStore);
        ArgumentNullException.ThrowIfNull(engagementStore);
        ArgumentNullException.ThrowIfNull(options);

        this.baselineStore = baselineStore;
        this.engagementStore = engagementStore;
        this.options = options;
    }

    /// <inheritdoc />
    public async Task<ComposedContext> ComposeAsync(ContextRequest request, string? revisionNote, int? pinnedEpoch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var catalogueId = options.Value.BaselineCatalogueId;
        var baselineJson = await baselineStore.GetBaselineCatalogueAsync(catalogueId, ct)
            ?? throw new InvalidOperationException($"Baseline catalogue '{catalogueId}' is not registered.");

        // S13.60 (doc 04 §4 step 3): read the pinned epoch, never ":current", when the run carries
        // one. A pin that no longer resolves is an evidential failure, not a fallback case — the
        // run was assembled from bytes the store can no longer produce.
        var snapshot = await engagementStore.GetDynamicContextSnapshotAsync(request.EngagementId, pinnedEpoch, ct);
        if (pinnedEpoch is not null && snapshot is null)
        {
            throw new InvalidOperationException(
                $"Dynamic context epoch {pinnedEpoch} for engagement '{request.EngagementId}' does not resolve; the run is pinned to a version the store cannot produce.");
        }

        var dynamicJson = snapshot?.Content ?? "{}";

        return new ComposedContext
        {
            BaselineContent = ContextContentFilter.Filter(baselineJson, request.BaselineComponents, "baseline_components"),
            DynamicContent = ContextContentFilter.Filter(dynamicJson, request.DynamicFields, "dynamic_fields"),
            RealTimeContent = BuildRealTimeContent(request, revisionNote),
            DynamicEpoch = snapshot?.Epoch,
            DynamicRef = snapshot?.Ref,
            DynamicContentHash = snapshot?.ContentHash,
        };
    }

    /// <summary>
    /// Builds the real-time tier's canonical-JSON content. Currently supports only the
    /// <c>"hitl-revision-note"</c> source (doc 06 §13, S4.6c): when requested and
    /// <paramref name="revisionNote"/> is present, renders <c>{"hitl_revision_note": "..."}</c>;
    /// otherwise <c>"{}"</c>.
    /// </summary>
    internal static string BuildRealTimeContent(ContextRequest request, string? revisionNote)
    {
        if (revisionNote is null || !request.RealTimeSources.Contains("hitl-revision-note", StringComparer.Ordinal))
        {
            return "{}";
        }

        var fields = new Dictionary<string, string> { ["hitl_revision_note"] = revisionNote };
        return JsonSerializer.Serialize(fields, CanonicalProfile.Options);
    }
}
