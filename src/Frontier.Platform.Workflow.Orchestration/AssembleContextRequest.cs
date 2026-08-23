using System.Text.Json.Serialization;
using Frontier.Platform.ContextAssembly;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="AssembleContextActivity"/> (S3.3 ADR-CR1): metadata for caching
/// strategy resolution and the content of each tier to be assembled into a context package.
/// All strings are pre-assembled from their respective sources (baseline catalogue,
/// engagement CRM data, real-time signals) by the caller.
/// </summary>
public sealed record AssembleContextRequest(
    [property: JsonPropertyName("caching_metadata"), JsonPropertyOrder(0)]
    CachingMetadata CachingMetadata,

    [property: JsonPropertyName("baseline_content"), JsonPropertyOrder(1)]
    string BaselineContent,

    [property: JsonPropertyName("dynamic_content"), JsonPropertyOrder(2)]
    string DynamicContent,

    [property: JsonPropertyName("real_time_content"), JsonPropertyOrder(3)]
    string RealTimeContent)
{
    /// <summary>Validates that all content strings are non-null.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CachingMetadata);
        ArgumentNullException.ThrowIfNull(BaselineContent);
        ArgumentNullException.ThrowIfNull(DynamicContent);
        ArgumentNullException.ThrowIfNull(RealTimeContent);
    }
}
