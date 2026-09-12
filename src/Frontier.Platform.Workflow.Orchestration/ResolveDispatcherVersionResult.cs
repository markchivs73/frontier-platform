using System.Text.Json.Serialization;
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
}
