using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Output of <see cref="ResolveDispatcherVersionActivity"/> (S13.18, ADR-E15 D2).
/// <para>
/// A <see langword="null"/> <see cref="Definition"/> means <b>no successor</b> — the engagement
/// keeps running the definition it is already pinned to (doc 16 §8's third bullet; ADR-E15 D2's
/// "the queue never stalls on a retired version", with the retirement monitor alerting). It never
/// means "the lookup failed": a resolver that cannot answer throws, and the generation boundary
/// fails loudly rather than silently pinning a dispatcher to a stale definition forever.
/// </para>
/// </summary>
public sealed record ResolveDispatcherVersionResult
{
    /// <summary>The definition the next generation should run, or <see langword="null"/> for "no successor — continue on the current version".</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("definition")]
    [JsonConverter(typeof(MigratingWorkflowDefinitionConverter))]
    public WorkflowDefinition? Definition { get; init; }

    /// <summary>
    /// The current governed contract budgets for the next generation (consumer S13.146), or null to carry the input's
    /// forward. Taken together with <see cref="ContractBudgetsVersion"/>, never apart.
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("contract_budgets")]
    public ContractBudgets? ContractBudgets { get; init; }

    /// <summary>The version of <see cref="ContractBudgets"/> (S13.147).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("contract_budgets_version")]
    public int? ContractBudgetsVersion { get; init; }

    /// <summary>The current governed rollover threshold, or null to carry the input's forward. Taken with <see cref="DispatcherSettingsVersion"/>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("dispatcher_rollover_threshold")]
    public int? DispatcherRolloverThreshold { get; init; }

    /// <summary>The version of the dispatcher-settings record <see cref="DispatcherRolloverThreshold"/> came from.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("dispatcher_settings_version")]
    public int? DispatcherSettingsVersion { get; init; }
}
