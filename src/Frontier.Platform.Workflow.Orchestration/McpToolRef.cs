using Frontier.Platform.Audit;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Parses an <see cref="Frontier.Platform.Workflow.Model.AgentTaskNode.ToolRefs"/> entry
/// (ADR-CD9, S13.7b) — <c>"{reverse-dns-server}/{tool}"</c>, e.g.
/// <c>"com.example.crm/tickets/get_ticket"</c>, matching
/// <see cref="Frontier.Platform.Workflow.Model.ToolCall.Name"/>'s wire convention — into
/// the registered server name the registry keys on and the tool name the MCP server exposes.
/// The server name is the registry's reverse-DNS form (<c>{namespace}/{name}</c>), so a full
/// reference always carries exactly two <c>/</c> and the <b>last</b> segment is the tool.
/// </summary>
public readonly record struct McpToolRef(string Server, string Tool)
{
    private const string ModelSafeNamePrefix = "mcp__";
    private const string ModelSafeNameSeparator = "__";

    /// <summary>
    /// Parses <paramref name="reference"/>, throwing <see cref="InvalidOperationException"/>
    /// (permanent failure — a malformed reference is a workflow-definition defect, never
    /// transient) unless it is <c>"{namespace}/{name}/{tool}"</c> with a dotted reverse-DNS
    /// namespace (ADR-CD9 naming).
    /// </summary>
    public static McpToolRef Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var lastSeparator = reference.LastIndexOf('/');
        var server = lastSeparator > 0 ? reference[..lastSeparator] : string.Empty;

        if (server.Count(c => c == '/') != 1 || !server.Contains('.', StringComparison.Ordinal) || lastSeparator == reference.Length - 1)
        {
            throw new InvalidOperationException($"Tool reference '{reference}' must be '{{reverse-dns-namespace}}/{{name}}/{{tool}}' (ADR-CD9), e.g. 'com.example.crm/tickets/get_ticket'.");
        }

        return new McpToolRef(server, reference[(lastSeparator + 1)..]);
    }

    /// <summary>The canonical <c>"{server}/{tool}"</c> wire form — the inverse of <see cref="Parse"/>.</summary>
    public string ToWireReference() => $"{Server}/{Tool}";

    /// <summary>
    /// An Anthropic-tool-name-safe alias — real Anthropic tool names must match
    /// <c>^[a-zA-Z0-9_-]{1,128}$</c>, which the wire form's <c>/</c> and <c>.</c> both
    /// violate (found running S9.25's gate test live against a real model for the first
    /// time: every tool-calling turn 400'd). The namespace's dots become single underscores
    /// and both <c>/</c> separators become <c>"__"</c>, e.g.
    /// <c>"mcp__io_frontier_demo__autotask__get_new_ticket"</c>. Round-trips back via
    /// <see cref="ParseModelSafeName"/>, which is unambiguous because DNS labels never
    /// contain <c>_</c>; the registered name segment must not contain <c>.</c> or <c>"__"</c>
    /// and the tool must not contain <c>.</c> (the same assumptions the pre-S13.7b
    /// convention documented for connector ids and tools).
    /// </summary>
    /// <remarks>
    /// Public for the same reason as <see cref="CanonicalOutputSchema"/>: an
    /// <see cref="IMcpToolCatalog"/> implementation presents tools to a model under this
    /// encoding and maps the model's calls back through <see cref="ParseModelSafeName"/>. Both
    /// halves must agree, so both live here rather than being reimplemented per adapter.
    /// </remarks>
    public string ToModelSafeName()
    {
        var namespaceSeparator = Server.IndexOf('/', StringComparison.Ordinal);
        var safeNamespace = Server[..namespaceSeparator].Replace('.', '_');
        return $"{ModelSafeNamePrefix}{safeNamespace}{ModelSafeNameSeparator}{Server[(namespaceSeparator + 1)..]}{ModelSafeNameSeparator}{Tool}";
    }

    /// <summary>
    /// Inverse of <see cref="ToModelSafeName"/>: after the fixed prefix, the first <c>"__"</c>
    /// closes the namespace (underscores restored to dots) and the second closes the name;
    /// the remainder is the tool verbatim (it may itself contain <c>"__"</c>, same as before).
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="safeName"/> isn't
    /// one of our own aliases.
    /// </summary>
    public static McpToolRef ParseModelSafeName(string safeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeName);

        if (!safeName.StartsWith(ModelSafeNamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Tool name '{safeName}' is not a recognised MCP tool alias.");
        }

        var remainder = safeName[ModelSafeNamePrefix.Length..];
        var namespaceEnd = remainder.IndexOf(ModelSafeNameSeparator, StringComparison.Ordinal);
        var nameStart = namespaceEnd + ModelSafeNameSeparator.Length;
        var nameEnd = namespaceEnd < 0 ? -1 : remainder.IndexOf(ModelSafeNameSeparator, nameStart, StringComparison.Ordinal);

        if (namespaceEnd <= 0 || nameEnd <= nameStart || nameEnd == remainder.Length - ModelSafeNameSeparator.Length)
        {
            throw new InvalidOperationException($"Tool name '{safeName}' is not a recognised MCP tool alias.");
        }

        var server = $"{remainder[..namespaceEnd].Replace('_', '.')}/{remainder[nameStart..nameEnd]}";
        return new McpToolRef(server, remainder[(nameEnd + ModelSafeNameSeparator.Length)..]);
    }
}
