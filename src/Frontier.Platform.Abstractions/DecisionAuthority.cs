using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// The authority a human decision was made under (ADR-PA53): the role the approver held and the scope it was held
/// at, so an audit can say not only who decided but by what right (doc 15 §4's binding: a principal, a role, a scope).
/// The consumer asserts it when it records the decision, as it asserts <see cref="HitlDecision.ApproverId"/>; the
/// platform carries it and signs it, and never checks it.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; its wire shape is pinned by the decision-authority golden files.")]
public sealed record DecisionAuthority
{
    /// <summary>The role through which the approver could decide, as the consumer's role catalogue names it.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>The scope the role was held at.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("scope")]
    public required DecisionScope Scope { get; init; }
}

/// <summary>
/// The scope a role was held at (ADR-PA53). Consumer-defined and opaque to the platform: <see cref="Kind"/> is a plain
/// string, not a smart enum, so a kind a newer consumer adds never makes an older worker throw while it reads a
/// recorded decision. Doc 15 §4's kinds (<c>deployment</c>, <c>client</c>, <c>engagement</c>) are examples only.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; its wire shape is pinned by the decision-authority golden files.")]
public sealed record DecisionScope
{
    /// <summary>What kind of scope, as the consumer names it.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>The scope's id; absent for a scope that needs none (a deployment-wide one).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}
