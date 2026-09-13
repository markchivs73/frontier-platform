namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A registered remote agent in a <see cref="RoleMapping"/>'s chain, reached over A2A (ADR-PA27;
/// PLATFORM-EVOLUTION-CANDIDATES E4/E6: an external agent is reached through <c>role</c>, not a new
/// node field). It names the consumer's registry resource and version; the pinned card snapshot
/// and its auth reference live there. Cost is a fixed amount per invocation — a remote agent
/// reports no tokens the platform can price — so budget ceilings stay meaningful (decision 1A).
/// </summary>
public sealed record AgentEntry : ChainEntry
{
    /// <summary>The provider value every agent entry carries.</summary>
    public const string A2aProvider = "a2a";

    /// <summary>The registry resource's reverse-DNS name, e.g. <c>"com.azure.foundry/echo"</c>.</summary>
    public required string ResourceName { get; init; }

    /// <summary>The registry resource version whose pinned card this entry binds to.</summary>
    public required string ResourceVersion { get; init; }

    /// <summary>The estimated cost of one invocation, in <see cref="ChainEntry.Currency"/> (scale 4). Must be positive: a zero cost would be admitted by every budget.</summary>
    public required decimal CostPerInvocation { get; init; }

    /// <inheritdoc />
    public override string TargetId => ResourceName;
}
