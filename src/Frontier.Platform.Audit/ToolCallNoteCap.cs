namespace Frontier.Platform.Audit;

/// <summary>
/// The 200-character cap on <see cref="ToolCall.Note"/> (ADR-PA28, ADR-E1 tonnage), checked by
/// <see cref="AuditRecord.Validate"/> and <see cref="SignedAuditRecord.Validate"/>.
/// </summary>
internal static class ToolCallNoteCap
{
    /// <summary>The longest note a tool call may carry.</summary>
    public const int MaxLength = 200;

    /// <summary>The violation text a record reports when any of its tool call notes exceeds the cap.</summary>
    public const string Violation = "every tool call note must be at most 200 characters.";

    /// <summary>Whether any tool call across <paramref name="invocations"/> carries a note longer than <see cref="MaxLength"/>.</summary>
    public static bool AnyExceeded(IEnumerable<AgentInvocation> invocations) =>
        invocations.SelectMany(invocation => invocation.ToolCalls).Any(call => call.Note is { Length: > MaxLength });
}
