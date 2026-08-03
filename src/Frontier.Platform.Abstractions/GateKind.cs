
namespace Frontier.Platform.Abstractions;

/// <summary>
/// The three <c>HumanGateNode</c> kinds (doc 06 §3): each selects default
/// allowed decisions and UI emphasis, but shares one pause/resume mechanism — there is
/// no separate code path per kind. Serializes as a snake_case string, identical to a
/// standard enum (doc 00 §3.5).
/// </summary>
public sealed class GateKind : SmartEnum<GateKind>
{
    /// <summary>Start-of-workflow gate: validates the engagement should run at all. Rejection cancels, no rollback.</summary>
    public static readonly GateKind Intake = new("intake");

    /// <summary>Post-section-block commercial approval. Rejection typically rolls back with revision notes.</summary>
    public static readonly GateKind Business = new("business");

    /// <summary>Pre-external-action gate, e.g. before an <c>McpToolNode</c> writes to CRM. Rejection skips-with-override or aborts the branch.</summary>
    public static readonly GateKind Technical = new("technical");

    private GateKind(string name)
        : base(name)
    {
    }
}
