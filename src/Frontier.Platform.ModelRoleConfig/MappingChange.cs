namespace Frontier.Platform.ModelRoleConfig;

/// <summary>A proposed new <see cref="RoleMapping"/> for a role, awaiting approval (doc 08 §7).</summary>
public sealed record MappingChange
{
    /// <summary>The role this change applies to.</summary>
    public required string RoleId { get; init; }

    /// <summary>The mapping that would become active if this change is approved.</summary>
    public required RoleMapping ProposedMapping { get; init; }

    /// <summary>Why this change is proposed (governance record, doc 08 §7).</summary>
    public required string Reason { get; init; }
}
