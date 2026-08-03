using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A <see cref="RoleMapping"/>'s exposure stage (doc 08 §2 principle 4, §7): new mappings
/// progress shadow → canary → fleet as evaluation evidence accumulates. Serializes as a
/// snake_case string via <c>SmartEnumJsonConverterFactory</c> (doc 01 ADR-C1).
/// </summary>
public sealed class RolloutRing : SmartEnum<RolloutRing>
{
    /// <summary>Invocations are duplicated to this mapping for comparison but never served.</summary>
    public static readonly RolloutRing Shadow = new("shadow");

    /// <summary>Served to an engagement-stable percentage of new executions (<see cref="RoleMapping.CanaryPercent"/>).</summary>
    public static readonly RolloutRing Canary = new("canary");

    /// <summary>Served to all new executions.</summary>
    public static readonly RolloutRing Fleet = new("fleet");

    private RolloutRing(string name)
        : base(name)
    {
    }
}
