using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// A consumer-owned view of an agent role for the chat designer agent (doc 14 §3; S9.27).
/// Deliberately minimal — the design agent only needs to know a role exists and what it's for,
/// so it can propose an <c>AgentTaskNode.Role</c> the Model-Role Config subsystem can actually
/// resolve; capability profiles and mapping governance are that subsystem's concern, not the
/// design agent's.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; values are exercised by the catalog adapter and chat-service tests.")]
public sealed record AgentRoleDescriptor
{
    /// <summary>The role identifier an <c>AgentTaskNode.Role</c> may reference, e.g. <c>"deep-reasoning"</c>.</summary>
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>What the role is for — the agent matches node intent against this.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// Supplies the agent roles the chat designer agent may propose for <c>AgentTaskNode.Role</c>
/// (doc 14 §3; S9.27 — added after the live walkthrough showed the agent inventing role names
/// with no catalogue to constrain it). A consumer-owned abstraction: the implementation adapts
/// the role catalogue from the Model-Role Config subsystem and is wired only in the composition
/// root, so the Definition Compiler stays within its library boundary — mirrors
/// <see cref="IApproverRoleCatalog"/> (ADR-CD5) and <see cref="IDesignerToolCatalog"/> (ADR-CD7).
/// </summary>
public interface IAgentRoleCatalog
{
    /// <summary>Returns the agent roles available for <c>AgentTaskNode.Role</c> proposals.</summary>
    Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct);
}
