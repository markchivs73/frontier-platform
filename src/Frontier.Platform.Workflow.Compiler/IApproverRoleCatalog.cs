using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// A consumer-owned view of an approver role for the chat designer agent (doc 14 §3, §8).
/// Carries the business metadata the agent matches gate intent against; the underlying role
/// catalogue lives in another subsystem (security config), so this descriptor is the boundary
/// the designer reasons over without the compiler taking a dependency on it.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; values are exercised by the catalog adapter and chat-service tests.")]
public sealed record ApproverRoleDescriptor
{
    /// <summary>The role identifier a <c>HumanGateNode.ApproverRoles</c> entry may reference.</summary>
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Human-readable role name.</summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    /// <summary>What the role is for — the agent matches designer intent against this.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Business area (e.g. <c>commercial</c>), if classified.</summary>
    [JsonPropertyName("business_area")]
    public string? BusinessArea { get; init; }

    /// <summary>Specific responsibilities (e.g. <c>budget-approval</c>).</summary>
    [JsonPropertyName("responsibilities")]
    public required IReadOnlyList<string> Responsibilities { get; init; }

    /// <summary>Gate kinds this role is appropriate for (advisory — keeps technical roles off Business gates).</summary>
    [JsonPropertyName("applicable_gate_kinds")]
    public required IReadOnlyList<string> ApplicableGateKinds { get; init; }

    /// <summary>Illustrative examples of when this role applies.</summary>
    [JsonPropertyName("examples")]
    public string? Examples { get; init; }
}

/// <summary>
/// Supplies the approver roles the chat designer agent may propose for <c>HumanGateNode</c>s
/// (doc 14 §3 <c>availableRoles</c>). A consumer-owned abstraction: the implementation adapts the
/// role catalogue from the security-config subsystem and is wired only in the composition root,
/// so the Definition Compiler stays within its library boundary.
/// </summary>
public interface IApproverRoleCatalog
{
    /// <summary>Returns the approver roles available for gate proposals.</summary>
    Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct);
}
