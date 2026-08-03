using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// What's commercially at stake if a <see cref="RoleDefinition"/>'s output is wrong
/// (doc 08 §4): drives the default mapping governance posture for the role. Serializes
/// as a snake_case string via <c>SmartEnumJsonConverterFactory</c> (doc 01 ADR-C1).
/// </summary>
public sealed class StakesLevel : SmartEnum<StakesLevel>
{
    /// <summary>Output feeds directly into commercial deliverables (e.g. Scope/Approach/Pricing).</summary>
    public static readonly StakesLevel Material = new("material");

    /// <summary>Output supports the pipeline but is checked or summarised before use.</summary>
    public static readonly StakesLevel Standard = new("standard");

    /// <summary>Output is infrastructural (e.g. embeddings for retrieval) with no direct client-facing content.</summary>
    public static readonly StakesLevel Mechanical = new("mechanical");

    private StakesLevel(string name)
        : base(name)
    {
    }
}
