using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>The three mentionable resource kinds (doc 14 §8a, ADR-CD8) — canonical wire values.</summary>
public static class MentionKind
{
    public const string AgentRole = "agent_role";
    public const string McpTool = "mcp_tool";
    public const string ApproverRole = "approver_role";
}

/// <summary>
/// A resource mention as submitted with a turn (doc 14 §8a, ADR-CD8): the exact canonical
/// identifier the composer's `@`/`.` grammar inserted — never a display label. <see cref="Ref"/>
/// matches the identifier the corresponding discovery catalogue keys on (agent-role id,
/// approver-role id, or the <c>{reverse-dns-server}/{tool}</c> ref, ADR-CD9's S13.7b convention).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain DTO; values exercised by ChatDesignerService validation tests.")]
public sealed record ResourceMention
{
    public required string Kind { get; init; }
    public required string Ref { get; init; }
}

/// <summary>
/// A mention after server-side validation against the live discovery catalogues (doc 14 §8a):
/// the server never trusts a client-asserted fact it can independently check (this doc's
/// existing posture on draft revisions/proposal state, §4/§9). Persisted on the turn so the
/// sent-turn UI can render a warning chip on an unresolved mention, and only <see cref="Resolved"/>
/// mentions are surfaced to the design agent as explicit user intent.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain DTO; values exercised by ChatDesignerService validation tests.")]
public sealed record ValidatedMention
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("resolved")]
    public required bool Resolved { get; init; }
}
