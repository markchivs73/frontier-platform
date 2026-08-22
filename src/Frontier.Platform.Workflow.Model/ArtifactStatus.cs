using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// State-machine status of a workflow section (doc 00 §3.4, doc 03). Serializes as a
/// snake_case string, identical to a standard enum (doc 00 §3.5).
/// <see cref="CanTransitionTo"/> encodes the Stage-1 PoC transition table; Cascade
/// Logic (doc 03) is the authority for any future refinement.
/// </summary>
public sealed class ArtifactStatus : SmartEnum<ArtifactStatus>
{
    /// <summary>The section has not been generated yet.</summary>
    public static readonly ArtifactStatus Empty = new("empty");

    /// <summary>The section has output pending approval.</summary>
    public static readonly ArtifactStatus Draft = new("draft");

    /// <summary>The section's content has been approved at its gate.</summary>
    public static readonly ArtifactStatus Approved = new("approved");

    /// <summary>An approved section is being regenerated after a cascade (doc 03 §9).</summary>
    public static readonly ArtifactStatus Regenerating = new("regenerating");

    /// <summary>The section is blocked on an upstream section completing first.</summary>
    public static readonly ArtifactStatus Waiting = new("waiting");

    private static readonly Dictionary<string, IReadOnlyList<ArtifactStatus>> Transitions = new()
    {
        [Empty.Name] = [Draft],
        [Draft.Name] = [Approved, Draft],
        [Approved.Name] = [Regenerating],
        [Regenerating.Name] = [Draft, Waiting],
        [Waiting.Name] = [Regenerating],
    };

    private ArtifactStatus(string name)
        : base(name)
    {
    }

    /// <summary>Whether this status may transition directly to <paramref name="target"/>.</summary>
    public bool CanTransitionTo(ArtifactStatus target) =>
        Transitions[Name].Contains(target);
}
