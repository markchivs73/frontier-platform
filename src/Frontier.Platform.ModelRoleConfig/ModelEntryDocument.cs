using Frontier.Platform.Serialization;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's wire shape for one <see cref="ModelEntry"/>
/// within a <see cref="RoleMappingDocument"/>'s <c>chain</c> (doc 08 §6).
/// </summary>
internal sealed record ModelEntryDocument
{
    /// <summary>The model provider, e.g. <c>"anthropic"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>The provider's model identifier, e.g. <c>"claude-fable-5"</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("model_id")]
    public required string ModelId { get; init; }

    /// <summary>Input token cost in GBP per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("input_cost_per_1k_gbp")]
    [DecimalPrecision(4)]
    public required decimal InputCostPer1kGbp { get; init; }

    /// <summary>Output token cost in GBP per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("output_cost_per_1k_gbp")]
    [DecimalPrecision(4)]
    public required decimal OutputCostPer1kGbp { get; init; }

    /// <summary>Cache-read token cost in GBP per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("cache_read_cost_per_1k_gbp")]
    [DecimalPrecision(4)]
    public required decimal CacheReadCostPer1kGbp { get; init; }

    /// <summary>The model's context window, in tokens.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("context_window")]
    public required int ContextWindow { get; init; }

    /// <summary>The model's maximum output tokens per invocation.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("max_output_tokens")]
    public required int MaxOutputTokens { get; init; }

    /// <summary>Link to an <c>ICachingStrategy</c> registry key (ADR-CA1), if this entry has one.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("caching_strategy")]
    public string? CachingStrategy { get; init; }

    /// <summary>Maps this wire entry onto its domain <see cref="ModelEntry"/>.</summary>
    internal ModelEntry ToDomain() => new()
    {
        Provider = Provider,
        ModelId = ModelId,
        InputCostPer1kGbp = InputCostPer1kGbp,
        OutputCostPer1kGbp = OutputCostPer1kGbp,
        CacheReadCostPer1kGbp = CacheReadCostPer1kGbp,
        ContextWindow = ContextWindow,
        MaxOutputTokens = MaxOutputTokens,
        CachingStrategy = CachingStrategy,
    };

    /// <summary>Maps a domain <see cref="ModelEntry"/> onto its wire entry.</summary>
    internal static ModelEntryDocument FromDomain(ModelEntry entry) => new()
    {
        Provider = entry.Provider,
        ModelId = entry.ModelId,
        InputCostPer1kGbp = entry.InputCostPer1kGbp,
        OutputCostPer1kGbp = entry.OutputCostPer1kGbp,
        CacheReadCostPer1kGbp = entry.CacheReadCostPer1kGbp,
        ContextWindow = entry.ContextWindow,
        MaxOutputTokens = entry.MaxOutputTokens,
        CachingStrategy = entry.CachingStrategy,
    };
}
