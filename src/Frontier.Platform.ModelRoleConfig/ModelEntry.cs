namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// One model in a <see cref="RoleMapping"/>'s chain (doc 08 §4): a concrete
/// provider/model plus the cost and capability metadata Guardrails and the estimate
/// builder consume (doc 08 §2 principle 7). Costs are in <see cref="Currency"/> per 1,000 tokens
/// at scale 4 (ADR-PA21).
/// </summary>
public sealed record ModelEntry
{
    /// <summary>The model provider, e.g. <c>"anthropic"</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's model identifier, e.g. <c>"claude-fable-5"</c>.</summary>
    public required string ModelId { get; init; }

    /// <summary>Input token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    public required decimal InputCostPer1k { get; init; }

    /// <summary>Output token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    public required decimal OutputCostPer1k { get; init; }

    /// <summary>Cache-read token cost in <see cref="Currency"/> per 1,000 tokens (scale 4).</summary>
    public required decimal CacheReadCostPer1k { get; init; }

    /// <summary>ISO 4217 code of the three cost figures, e.g. <c>"USD"</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>The model's context window, in tokens.</summary>
    public required int ContextWindow { get; init; }

    /// <summary>The model's maximum output tokens per invocation.</summary>
    public required int MaxOutputTokens { get; init; }

    /// <summary>Link to an <c>ICachingStrategy</c> registry key (ADR-CA1), if this entry has one.</summary>
    public string? CachingStrategy { get; init; }
}
