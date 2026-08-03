namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The outcome of <see cref="IModelResolver.ResolveAsync"/> (doc 08 §4): the concrete
/// model an <c>InvokeAgentActivity</c> (S4.2) should bind to, plus the audit fields
/// (role, mapping version, chain position) that make the resolution an empirical record
/// (doc 08 §2 principle 7).
/// </summary>
public sealed record ResolvedModel
{
    /// <summary>The role that was resolved.</summary>
    public required string RoleId { get; init; }

    /// <summary>The pinned mapping version this resolution was made under.</summary>
    public required int MappingVersion { get; init; }

    /// <summary>The resolved model's provider, e.g. <c>"anthropic"</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The resolved model's identifier, e.g. <c>"claude-fable-5"</c>.</summary>
    public required string ModelId { get; init; }

    /// <summary>The resolved model's version, if the provider reports one (feeds caching-strategy resolution, ADR-CA1).</summary>
    public string? ModelVersion { get; init; }

    /// <summary>Position in the mapping's chain that was served: 0 = primary, &gt;0 = fallback (doc 08 §4 ADR-M2, an alarm signal when sustained).</summary>
    public required int ChainPosition { get; init; }

    /// <summary>The resolved chain entry's cost/capability metadata, for Guardrails.</summary>
    public required ModelEntry Entry { get; init; }
}
