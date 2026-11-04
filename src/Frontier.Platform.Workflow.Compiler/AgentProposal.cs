using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Serialization;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The structured proposal the design agent returns each turn (doc 14 §4.1): a <em>complete</em>
/// <see cref="WorkflowDefinition"/> (closed-world generation — never a patch), a short reason, and
/// the agent's own claim of which nodes it touched (display hint only; the authoritative diff is
/// computed server-side in S9.10).
/// </summary>
public sealed record AgentProposal
{
    /// <summary>Short explanation of what the agent changed and why.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>The complete proposed workflow definition.</summary>
    [JsonPropertyName("definition")]
    public WorkflowDefinition? Definition { get; init; }

    /// <summary>The agent's claim of which node ids it added/changed (advisory).</summary>
    [JsonPropertyName("changed_node_ids")]
    public IReadOnlyList<string>? ChangedNodeIds { get; init; }
}

/// <summary>
/// Parses the design agent's raw text into an <see cref="AgentProposal"/> (doc 14 §4). The agent is
/// instructed to return JSON only, but models occasionally wrap it in markdown fences — those are
/// stripped before parsing. A parse failure is not an error: the caller falls back to treating the
/// response as plain reasoning (the agent can hallucinate; the protocol degrades gracefully).
/// </summary>
public static class AgentProposalParser
{
    /// <summary>Attempts to parse a structured proposal; returns <c>false</c> if absent or malformed.</summary>
    public static bool TryParse(string? raw, out AgentProposal? proposal) => TryParse(raw, out proposal, out _);

    /// <summary>
    /// Attempts to parse a structured proposal, also reporting every key the proposal carries that the
    /// schema does not define (S13.210). The serializer drops such a key silently, so the proposal
    /// still parses; <paramref name="unknownKeys"/> is how the caller learns the model invented or
    /// misspelled a field and can send it back for repair.
    /// </summary>
    internal static bool TryParse(string? raw, out AgentProposal? proposal, out IReadOnlyList<string> unknownKeys)
    {
        proposal = null;
        unknownKeys = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            using var document = JsonDocument.Parse(StripFences(raw));
            proposal = JsonSerializer.Deserialize<AgentProposal>(document.RootElement, CanonicalProfile.Options);
            unknownKeys = UnknownJsonKeys.Find(document.RootElement, typeof(AgentProposal), CanonicalProfile.Options);
            return proposal?.Definition is not null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A malformed proposal is not an error (doc 14 §4 — degrade to plain reasoning).
            // A node missing its `node_type` polymorphic discriminator surfaces as
            // NotSupportedException (not JsonException), so both must be caught or the whole chat
            // turn 500s instead of falling back to the ParseFailureMessage recovery path.
            return false;
        }
    }

    /// <summary>Strips a leading/trailing markdown code fence (```json … ```), returning the inner JSON.</summary>
    internal static string StripFences(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewline < 0) return trimmed;

        var body = trimmed[(firstNewline + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return (lastFence >= 0 ? body[..lastFence] : body).Trim();
    }
}
