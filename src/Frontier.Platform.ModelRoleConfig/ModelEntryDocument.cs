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

    /// <summary>Input token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("input_cost_per_1k")]
    [DecimalPrecision(4)]
    public required decimal InputCostPer1k { get; init; }

    /// <summary>Output token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("output_cost_per_1k")]
    [DecimalPrecision(4)]
    public required decimal OutputCostPer1k { get; init; }

    /// <summary>Cache-read token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("cache_read_cost_per_1k")]
    [DecimalPrecision(4)]
    public required decimal CacheReadCostPer1k { get; init; }

    /// <summary>ISO 4217 code of the three cost figures, e.g. <c>"USD"</c> (ADR-PA21).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>The model's context window, in tokens.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("context_window")]
    public required int ContextWindow { get; init; }

    /// <summary>The model's maximum output tokens per invocation.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("max_output_tokens")]
    public required int MaxOutputTokens { get; init; }

    /// <summary>Link to an <c>ICachingStrategy</c> registry key (ADR-CA1), if this entry has one.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("caching_strategy")]
    public string? CachingStrategy { get; init; }

    /// <summary>Maps this wire entry onto its domain <see cref="ModelEntry"/>.</summary>
    internal ModelEntry ToDomain() => new()
    {
        Provider = Provider,
        ModelId = ModelId,
        InputCostPer1k = InputCostPer1k,
        OutputCostPer1k = OutputCostPer1k,
        CacheReadCostPer1k = CacheReadCostPer1k,
        Currency = Currency,
        ContextWindow = ContextWindow,
        MaxOutputTokens = MaxOutputTokens,
        CachingStrategy = CachingStrategy,
    };

    /// <summary>Maps a domain <see cref="ModelEntry"/> onto its wire entry.</summary>
    internal static ModelEntryDocument FromDomain(ModelEntry entry) => new()
    {
        Provider = entry.Provider,
        ModelId = entry.ModelId,
        InputCostPer1k = entry.InputCostPer1k,
        OutputCostPer1k = entry.OutputCostPer1k,
        CacheReadCostPer1k = entry.CacheReadCostPer1k,
        Currency = entry.Currency,
        ContextWindow = entry.ContextWindow,
        MaxOutputTokens = entry.MaxOutputTokens,
        CachingStrategy = entry.CachingStrategy,
    };
}
