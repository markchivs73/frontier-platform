using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// The contract validator's budgets as governance data (ADR-PA43, K7): how costly a rule may be,
/// how many iterations it may take, how deeply a payload may nest and how many violations are
/// reported. A run pins one record at its start and every validating step reads that record, so a
/// change applies to new runs only and a recorded run replays with the budgets it ran under.
/// <para>
/// There is no wall-clock field: the one time limit, the schema <c>pattern</c> timeout, describes the
/// deployment and stays in its configuration. <see cref="MaxRuleCost"/> is checked when a contract is
/// registered, never at run time; a run carries it so its evidence names the whole record.
/// </para>
/// </summary>
public sealed record ContractBudgets : IVersionedContract
{
    /// <summary>The deepest <see cref="MaxPayloadDepth"/> a record may set.</summary>
    public const int PayloadDepthCeiling = 256;

    /// <summary>The highest <see cref="MaxViolations"/> a record may set.</summary>
    public const int ViolationsCeiling = 1000;

    /// <summary>The platform's defaults: what a run with no pinned record validates under.</summary>
    public static ContractBudgets Default { get; } = new()
    {
        MaxRuleCost = 1_000_000,
        MaxRuleIterations = 100_000,
        MaxPayloadDepth = 64,
        MaxViolations = 20,
    };

    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    /// The highest estimated cost a rule may have, in Celly's units (cel-go's), checked when the
    /// contract is registered. The default is Kubernetes' per-expression limit.
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("max_rule_cost")]
    public required int MaxRuleCost { get; init; }

    /// <summary>How many comprehension iterations one rule may take on one payload; running out is a permanent violation.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("max_rule_iterations")]
    public required int MaxRuleIterations { get; init; }

    /// <summary>The deepest nesting a payload may have; deeper is a violation, never a crash.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("max_payload_depth")]
    public required int MaxPayloadDepth { get; init; }

    /// <summary>The most violations reported for one payload.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("max_violations")]
    public required int MaxViolations { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();
        AddIfOutside(violations, "max_rule_cost", MaxRuleCost, int.MaxValue);
        AddIfOutside(violations, "max_rule_iterations", MaxRuleIterations, int.MaxValue);
        AddIfOutside(violations, "max_payload_depth", MaxPayloadDepth, PayloadDepthCeiling);
        AddIfOutside(violations, "max_violations", MaxViolations, ViolationsCeiling);
        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(ContractBudgets), violations);
        }
    }

    private static void AddIfOutside(List<string> violations, string name, int value, int ceiling)
    {
        if (value < 1 || value > ceiling)
        {
            violations.Add($"{name} must be between 1 and {ceiling}");
        }
    }
}
