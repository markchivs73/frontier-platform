using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// What serves a <see cref="RoleDefinition"/> (ADR-PA40): a model chain or an agent chain. Declared
/// when the role is defined and never changed — moving between the two means a new role — so a
/// mapping's chain kind (ADR-PA27) can be checked against it. Serializes as a snake_case string via
/// <c>SmartEnumJsonConverterFactory</c> (doc 01 ADR-C1); an unknown wire value is refused.
/// </summary>
public sealed class RoleBacking : SmartEnum<RoleBacking>
{
    /// <summary>The role is served by a chain of models; it states capability requirements.</summary>
    public static readonly RoleBacking Model = new("model");

    /// <summary>The role is served by a chain of registered agents; it declares both contract refs.</summary>
    public static readonly RoleBacking Agent = new("agent");

    private RoleBacking(string name)
        : base(name)
    {
    }
}
