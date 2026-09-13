using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's wire shape for one <see cref="ChainEntry"/> within a
/// <see cref="RoleMappingDocument"/>'s <c>chain</c> (doc 08 §6, ADR-PA27). One record carries both kinds:
/// <see cref="Target"/> discriminates, and an <b>absent target reads as a model</b> — the migration adapter
/// for every mapping written before ADR-PA27. Those bytes do not change, because a model entry never
/// writes <see cref="Target"/> or the agent fields, and the canonical profile omits nulls.
/// </summary>
internal sealed record ChainEntryDocument
{
    /// <summary>The explicit model discriminator (equivalent to an absent target).</summary>
    internal const string ModelTarget = "model";

    /// <summary>The agent discriminator.</summary>
    internal const string AgentTarget = "agent";

    /// <summary>The provider, e.g. <c>"anthropic"</c>, or <c>"a2a"</c> for an agent.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>A model's identifier; absent for an agent.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    /// <summary>A model's input token cost per 1,000 tokens (scale 4); absent for an agent.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("input_cost_per_1k")]
    [DecimalPrecision(4)]
    public decimal? InputCostPer1k { get; init; }

    /// <summary>A model's output token cost per 1,000 tokens (scale 4); absent for an agent.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("output_cost_per_1k")]
    [DecimalPrecision(4)]
    public decimal? OutputCostPer1k { get; init; }

    /// <summary>A model's cache-read token cost per 1,000 tokens (scale 4); absent for an agent.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("cache_read_cost_per_1k")]
    [DecimalPrecision(4)]
    public decimal? CacheReadCostPer1k { get; init; }

    /// <summary>ISO 4217 code of the entry's costs, e.g. <c>"USD"</c> (ADR-PA21).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>A model's context window, in tokens; absent for an agent.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("context_window")]
    public int? ContextWindow { get; init; }

    /// <summary>A model's maximum output tokens per invocation; absent for an agent.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    /// <summary>A model's link to an <c>ICachingStrategy</c> registry key (ADR-CA1), if it has one.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("caching_strategy")]
    public string? CachingStrategy { get; init; }

    /// <summary><c>"agent"</c> for an agent entry; absent (or <c>"model"</c>) for a model.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>A model's service endpoint for providers that need one, e.g. <c>azure-openai</c>.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>A model's deployment name for providers that address models by deployment, e.g. <c>azure-openai</c>.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("deployment")]
    public string? Deployment { get; init; }

    /// <summary>An agent's registry resource name.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("resource_name")]
    public string? ResourceName { get; init; }

    /// <summary>An agent's registry resource version.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("resource_version")]
    public string? ResourceVersion { get; init; }

    /// <summary>An agent's cost per invocation (scale 4).</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("cost_per_invocation")]
    [DecimalPrecision(4)]
    public decimal? CostPerInvocation { get; init; }

    /// <summary>Maps this wire entry onto its domain entry, refusing an unknown target or a missing field as a permanent contract violation.</summary>
    internal ChainEntry ToDomain() => Target switch
    {
        null or ModelTarget => ToModel(),
        AgentTarget => ToAgent(),
        _ => throw Violation($"target '{Target}' is not '{ModelTarget}' or '{AgentTarget}'."),
    };

    /// <summary>Maps a model entry, requiring every model field.</summary>
    internal ModelEntry ToModel() => new()
    {
        Provider = Provider,
        Currency = Currency,
        ModelId = ModelId ?? throw Missing("model_id"),
        InputCostPer1k = InputCostPer1k ?? throw Missing("input_cost_per_1k"),
        OutputCostPer1k = OutputCostPer1k ?? throw Missing("output_cost_per_1k"),
        CacheReadCostPer1k = CacheReadCostPer1k ?? throw Missing("cache_read_cost_per_1k"),
        ContextWindow = ContextWindow ?? throw Missing("context_window"),
        MaxOutputTokens = MaxOutputTokens ?? throw Missing("max_output_tokens"),
        CachingStrategy = CachingStrategy,
        Endpoint = Endpoint is null ? null : new Uri(Endpoint, UriKind.Absolute),
        Deployment = Deployment,
    };

    /// <summary>Maps an agent entry, requiring every agent field.</summary>
    internal AgentEntry ToAgent() => new()
    {
        Provider = Provider,
        Currency = Currency,
        ResourceName = ResourceName ?? throw Missing("resource_name"),
        ResourceVersion = ResourceVersion ?? throw Missing("resource_version"),
        CostPerInvocation = CostPerInvocation ?? throw Missing("cost_per_invocation"),
    };

    /// <summary>Maps a domain entry onto its wire entry.</summary>
    internal static ChainEntryDocument FromDomain(ChainEntry entry) =>
        entry is AgentEntry agent ? FromAgent(agent) : FromModel((ModelEntry)entry);

    /// <summary>Maps a model entry. <see cref="Target"/> stays absent, so the bytes are exactly those written before ADR-PA27.</summary>
    internal static ChainEntryDocument FromModel(ModelEntry model) => new()
    {
        Provider = model.Provider,
        ModelId = model.ModelId,
        InputCostPer1k = model.InputCostPer1k,
        OutputCostPer1k = model.OutputCostPer1k,
        CacheReadCostPer1k = model.CacheReadCostPer1k,
        Currency = model.Currency,
        ContextWindow = model.ContextWindow,
        MaxOutputTokens = model.MaxOutputTokens,
        CachingStrategy = model.CachingStrategy,
        Endpoint = model.Endpoint?.AbsoluteUri,
        Deployment = model.Deployment,
    };

    /// <summary>Maps an agent entry.</summary>
    internal static ChainEntryDocument FromAgent(AgentEntry agent) => new()
    {
        Provider = agent.Provider,
        Currency = agent.Currency,
        Target = AgentTarget,
        ResourceName = agent.ResourceName,
        ResourceVersion = agent.ResourceVersion,
        CostPerInvocation = agent.CostPerInvocation,
    };

    /// <summary>A missing required field for the entry's target.</summary>
    internal static ContractViolationException Missing(string field) => Violation($"{field} is required for this chain entry's target.");

    /// <summary>A permanent violation naming this wire shape.</summary>
    internal static ContractViolationException Violation(string message) => new(nameof(ChainEntryDocument), [message]);
}
