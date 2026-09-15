namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Input to <see cref="IModelResolver.ResolveAsync"/> (doc 08 §5): the role to resolve,
/// the engagement (for engagement-stable canary assignment), and optionally the mapping
/// version pinned at execution start.
/// </summary>
public sealed record ResolutionRequest
{
    /// <summary>The role to resolve.</summary>
    public required string RoleId { get; init; }

    /// <summary>The engagement this resolution is for (doc 08 §5: canary assignment is engagement-stable).</summary>
    public required string EngagementId { get; init; }

    /// <summary>The mapping version pinned at execution start, or <see langword="null"/> to resolve the active mapping (doc 08 §5).</summary>
    public int? MappingVersion { get; init; }

    /// <summary>
    /// The pin taken at execution start (ADR-PA29), or <see langword="null"/> for the unpinned
    /// behaviour. When set, resolution reads exactly <see cref="ModelRolePin.MappingVersion"/> and
    /// walks its fallback chain; rings are not re-evaluated, because the pin already records the
    /// version that was served. Takes precedence over <see cref="MappingVersion"/>.
    /// </summary>
    public ModelRolePin? Pin { get; init; }
}
