using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The platform's half of the agent-entry guard (ADR-PA27): a chain is all-model or all-agent, and
/// every agent entry names its resource and a positive per-invocation cost. Enforced when a mapping
/// is read and when it is resolved, so no path serves an ill-shaped chain. Whether the named
/// resource is <i>active</i> is the consumer's check — only the consumer can see its registry.
/// </summary>
internal static class ChainShape
{
    /// <summary>Returns <paramref name="mapping"/>, or throws a permanent <see cref="ContractViolationException"/> listing every shape violation.</summary>
    internal static RoleMapping EnsureValid(RoleMapping mapping)
    {
        var violations = Violations(mapping.Chain);
        return violations.Count == 0
            ? mapping
            : throw new ContractViolationException(nameof(RoleMapping), violations);
    }

    /// <summary>Every shape violation in <paramref name="chain"/>.</summary>
    internal static List<string> Violations(IReadOnlyList<ChainEntry> chain)
    {
        var violations = new List<string>();
        if (chain.OfType<ModelEntry>().Any() && chain.OfType<AgentEntry>().Any())
        {
            violations.Add("chain must be all-model or all-agent, never mixed (ADR-PA27).");
        }

        foreach (var agent in chain.OfType<AgentEntry>())
        {
            AddAgentViolations(agent, violations);
        }

        return violations;
    }

    /// <summary>An agent entry's own rules: the A2A provider, a resource name and version, and a positive cost.</summary>
    internal static void AddAgentViolations(AgentEntry agent, List<string> violations)
    {
        if (!string.Equals(agent.Provider, AgentEntry.A2aProvider, StringComparison.Ordinal))
        {
            violations.Add($"agent entry '{agent.ResourceName}' must have provider '{AgentEntry.A2aProvider}'.");
        }

        if (string.IsNullOrWhiteSpace(agent.ResourceName) || string.IsNullOrWhiteSpace(agent.ResourceVersion))
        {
            violations.Add("agent entry must name its registry resource and version.");
        }

        if (agent.CostPerInvocation <= 0m)
        {
            violations.Add($"agent entry '{agent.ResourceName}' must have a positive cost_per_invocation; a zero cost is admitted by every budget (decision 1A).");
        }
    }
}
