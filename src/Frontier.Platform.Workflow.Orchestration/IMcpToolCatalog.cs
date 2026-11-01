using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Resolves an <see cref="Frontier.Platform.Workflow.Model.AgentTaskNode.ToolRefs"/> list
/// into live <see cref="AITool"/> instances a MAF <c>ChatClientAgent</c> can call directly
/// (ADR-CD6: MAF-native tool-calling, no dedicated tool-invocation node). S9.25 resolves
/// against a hardcoded two-connector map; S9.26 replaces the lookup mechanism behind this
/// same interface.
/// </summary>
public interface IMcpToolCatalog
{
    /// <summary>
    /// Resolves each <c>"{reverse-dns-server}/{tool}"</c> entry in <paramref name="toolRefs"/>
    /// (ADR-CD9, S13.7b) to the matching MCP tool, connecting to each referenced server at
    /// most once per call.
    /// Returns <c>[]</c> for an empty <paramref name="toolRefs"/> — no MCP round trip is made.
    /// <para>ADR-PA42: <paramref name="pins"/> holds what the published definition pins for its tools,
    /// at most one entry per tool ref. A tool with a pin is built from that pin: its schema and its
    /// description come from <see cref="McpToolPin.InputContract"/>, and its write classification is
    /// <see cref="McpToolPin.Writes"/>. Only a fact the pin leaves null, a tool with no pin, or a call
    /// with <paramref name="pins"/> null (a definition published before the pins) falls back to the
    /// newest active registry version, as before. The server's endpoint always resolves on the newest
    /// active version: an endpoint is not a contract.</para>
    /// S9.38c: when <paramref name="engagementId"/> is a sandbox test-run's per-run engagement
    /// (<c>SANDBOX-</c> prefix), any tool classified as a write is returned wrapped so calling it
    /// never reaches the real connector (doc 13 §5) — read tools are unaffected. A tool that cannot be
    /// classified is treated as a write. ADR-PA20: this used to be read off the execution id, which no
    /// longer spells the engagement.
    /// </summary>
    Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, IReadOnlyList<McpToolPin>? pins, string engagementId, CancellationToken ct);
}
