using System.Text.Json.Serialization;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// What a <see cref="HumanGateNode"/> emits when it is decided (ADR-E27 D13): the engine-owned
/// contract <c>frontier/gate-decision/1.0</c>. The engine writes it to the gate's node output, so a
/// data edge from a gate carries who decided what to the nodes after it (ADR-E8's attribution
/// chain). Registry copy: <see cref="EngineContracts.GateDecision"/>.
/// </summary>
public sealed record GateDecision : IVersionedContract
{
    /// <summary>The ref this contract is registered and embedded under.</summary>
    public const string ContractRef = "frontier/gate-decision/1.0";

    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The deciding gate's node id.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("gate_id")]
    public required string GateId { get; init; }

    /// <summary>What was decided.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("decision")]
    public required DecisionKind Decision { get; init; }

    /// <summary>The resolved identity of the human who decided.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("approver_id")]
    public required string ApproverId { get; init; }

    /// <summary>When the decision was recorded, in UTC.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("decided_at_utc")]
    public required DateTime DecidedAtUtc { get; init; }

    /// <summary>The approver's notes, if any.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>For a rejection, the node the run rolls back to, if any.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("rollback_to_node_id")]
    public string? RollbackToNodeId { get; init; }

    /// <summary>The decision a gate records, as the contract it emits.</summary>
    public static GateDecision From(HitlDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new GateDecision
        {
            GateId = decision.GateId,
            Decision = decision.Kind,
            ApproverId = decision.ApproverId,
            DecidedAtUtc = decision.DecidedAtUtc,
            Notes = decision.Notes,
            RollbackToNodeId = decision.RollbackToNodeId,
        };
    }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(GateId))
        {
            violations.Add("gate_id is required");
        }

        if (string.IsNullOrWhiteSpace(ApproverId))
        {
            violations.Add("approver_id is required");
        }

        if (RollbackToNodeId is not null && Decision != DecisionKind.Reject)
        {
            violations.Add("rollback_to_node_id is only set on a rejection");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(GateDecision), violations);
        }
    }
}
