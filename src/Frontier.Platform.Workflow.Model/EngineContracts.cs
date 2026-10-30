using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The registry snapshots of the engine-owned contracts that nodes and edges can name by ref
/// (ADR-E27 D13). They are CLR types inside ADR-E2's closed engine set, with their schemas written
/// out here so edges and projections check them like any other contract. Publish embeds them from
/// here rather than from the registry, so a run never depends on the registry holding them. The
/// registry carries the same snapshots so designers can find them.
/// </summary>
public static class EngineContracts
{
    /// <summary>The snapshot of <see cref="Model.GateDecision"/>, <c>frontier/gate-decision/1.0</c>.</summary>
    public static ContractSnapshot GateDecision { get; } = new()
    {
        ContractRef = Model.GateDecision.ContractRef,
        Schema = Parse(GateDecisionSchema),
        Rules = [],
        Hash = "sha256:001e2bed826ff14974d73e6728cf21bd3a24f020db313353184d19752f197fbd",
    };

    /// <summary>Every engine-owned snapshot, sorted by ref.</summary>
    public static IReadOnlyList<ContractSnapshot> All { get; } = [GateDecision];

    /// <summary>The engine-owned snapshot registered under <paramref name="contractRef"/>, or null when the engine owns no such contract.</summary>
    public static ContractSnapshot? Find(string contractRef) =>
        All.FirstOrDefault(c => string.Equals(c.ContractRef, contractRef, StringComparison.Ordinal));

    private static JsonElement Parse(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private const string GateDecisionSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "Gate decision",
          "description": "What a human gate emits when a person decides it: the decision, who made it, when, and their notes. A node after the gate reads it to act on, or record, the human's call. It is written by the engine, never by an agent, so do not use it as an agent's output.",
          "type": "object",
          "properties": {
            "schema_version": { "description": "The contract's wire version.", "const": "1.0" },
            "gate_id": { "description": "The node id of the gate that was decided.", "type": "string", "minLength": 1 },
            "decision": { "description": "What the person decided.", "enum": ["approve", "reject", "escalate"] },
            "approver_id": { "description": "The resolved identity of the person who decided.", "type": "string", "minLength": 1 },
            "decided_at_utc": { "description": "When the decision was recorded, in UTC with milliseconds.", "type": "string", "pattern": "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$" },
            "notes": { "description": "The person's notes, when they left any.", "type": "string" },
            "rollback_to_node_id": { "description": "For a rejection, the node the run rolls back to, when the gate names one.", "type": "string", "minLength": 1 }
          },
          "required": ["schema_version", "gate_id", "decision", "approver_id", "decided_at_utc"],
          "additionalProperties": false,
          "examples": [
            {
              "schema_version": "1.0",
              "gate_id": "approve-quote",
              "decision": "approve",
              "approver_id": "user:4f1c2a",
              "decided_at_utc": "2026-10-30T09:15:00.000Z",
              "notes": "Pricing checked against the rate card."
            }
          ]
        }
        """;
}
