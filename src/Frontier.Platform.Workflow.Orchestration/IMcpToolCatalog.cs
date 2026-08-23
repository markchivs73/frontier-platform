using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Resolves an <see cref="Frontier.Platform.Workflow.Model.AgentTaskNode.ToolRefs"/> list
/// into live <see cref="AITool"/> instances a MAF <c>ChatClientAgent</c> can call directly
/// (ADR-CD6: MAF-native tool-calling, no dedicated tool-invocation node). S9.25 resolves
/// against a hardcoded two-connector map; S9.26 replaces the lookup mechanism behind this
/// same interface.
/// </summary>
internal interface IMcpToolCatalog
{
    /// <summary>
    /// Resolves each <c>"{reverse-dns-server}/{tool}"</c> entry in <paramref name="toolRefs"/>
    /// (ADR-CD9, S13.7b) to the matching MCP tool, connecting to each referenced server at
    /// most once per call.
    /// Returns <c>[]</c> for an empty <paramref name="toolRefs"/> — no MCP round trip is made.
    /// S9.38c: when <paramref name="executionId"/> is a sandbox test-run (<c>SANDBOX-</c>
    /// prefix), any tool classified as a write (<see cref="McpSandboxWriteTools"/>) is
    /// returned wrapped so calling it never reaches the real connector (doc 13 §5) —
    /// read tools are unaffected.
    /// </summary>
    Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, string executionId, CancellationToken ct);
}
