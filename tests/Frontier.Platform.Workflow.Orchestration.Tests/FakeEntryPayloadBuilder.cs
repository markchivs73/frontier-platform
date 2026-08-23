using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.12c: stands in for the workload's <see cref="IEntryPayloadBuilder"/> (the real one is
/// Host-side — <c>EngagementBriefEntryPayloadBuilder</c> — because the mapping is workload
/// vocabulary). Mirrors its behaviour so the pipeline suite keeps exercising the entry-node
/// path without the engine depending on a workload contract.
/// </summary>
internal sealed class FakeEntryPayloadBuilder : IEntryPayloadBuilder
{
    /// <inheritdoc />
    public string BuildEntryPayload(string dynamicContentJson)
    {
        using var document = JsonDocument.Parse(dynamicContentJson);
        if (!document.RootElement.TryGetProperty("engagement_brief", out var narrative))
        {
            throw new ContractViolationException(nameof(BriefArtifact), ["missing dynamic context field 'engagement_brief'."]);
        }

        return JsonSerializer.Serialize(new BriefArtifact { Narrative = narrative.GetString() ?? string.Empty }, CanonicalProfile.Options);
    }
}
