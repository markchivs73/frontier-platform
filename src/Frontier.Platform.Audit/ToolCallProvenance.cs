using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// How the platform came to know about a <see cref="ToolCall"/> (ADR-PA28). Serializes as a
/// snake_case string, identical to a standard enum (doc 00 §3.5). The platform's own writers
/// (the MCP path, S9.25) never set it, so a record written before the field carries no
/// provenance and reads as <see cref="Observed"/>.
/// </summary>
public sealed class ToolCallProvenance : SmartEnum<ToolCallProvenance>
{
    /// <summary>The platform made the call itself and saw it happen.</summary>
    public static readonly ToolCallProvenance Observed = new("observed");

    /// <summary>
    /// A remote agent claimed the call in its reply; the platform never saw it. A claim is
    /// never rendered as observed evidence (K6).
    /// </summary>
    public static readonly ToolCallProvenance SelfReported = new("self_reported");

    private ToolCallProvenance(string name)
        : base(name)
    {
    }
}
