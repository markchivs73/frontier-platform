namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// One difference between two sets of activity shapes. A breaking change is one a run recorded
/// under the older shape cannot survive: an activity removed, a member lost or renamed, a member
/// reshaped, or a required member gained. Everything else is additive.
/// </summary>
internal sealed record ShapeChange(string Activity, string Description, bool Breaking)
{
    /// <inheritdoc />
    public override string ToString() => $"{(Breaking ? "BREAKING" : "additive")} {Activity}: {Description}";
}
