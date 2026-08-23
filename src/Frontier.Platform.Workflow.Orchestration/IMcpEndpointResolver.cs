namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Resolves a registered MCP server's reverse-DNS name to its invocation endpoint
/// (ADR-CD9, S13.7b: endpoints live in the registry record's <c>server_json</c>, not
/// configuration — <c>McpConnectorOptions</c> is retired). A consumer-owned port: the
/// implementation reads the resource registry and is wired only in the composition root,
/// so this library stays within its boundary (no dependency on the Registry library).
/// </summary>
public interface IMcpEndpointResolver
{
    /// <summary>
    /// Returns the invocation endpoint of <paramref name="serverName"/>'s newest
    /// <c>active</c> registered version. Throws <see cref="InvalidOperationException"/>
    /// (permanent failure — a definition referencing an unregistered or endpoint-less
    /// server is a defect, never transient) when no active version carries one.
    /// </summary>
    Task<Uri> ResolveAsync(string serverName, CancellationToken ct);
}
