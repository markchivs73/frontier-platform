namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// Compares two sets of activity shapes under S13.185's rule: a run recorded under
/// <c>before</c> must still replay against <c>after</c> within one generation. A new required
/// member is breaking only beneath a parent the old shape already had, since a parent the old
/// shape lacked never appears in old history. An enum that only gains values is additive.
/// </summary>
internal static class ShapeComparer
{
    /// <summary>Every difference from <paramref name="before"/> to <paramref name="after"/>, ordered by activity.</summary>
    public static IReadOnlyList<ShapeChange> Compare(IEnumerable<ActivityShape> before, IEnumerable<ActivityShape> after)
    {
        var old = before.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var current = after.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var changes = new List<ShapeChange>();
        foreach (var (name, shape) in old)
        {
            changes.AddRange(current.TryGetValue(name, out var now)
                ? CompareActivity(shape, now)
                : [new ShapeChange(name, "activity removed or renamed", Breaking: true)]);
        }

        changes.AddRange(current.Keys.Where(name => !old.ContainsKey(name)).Select(name => new ShapeChange(name, "activity added", Breaking: false)));
        return [.. changes.OrderBy(c => c.Activity, StringComparer.Ordinal)];
    }

    /// <summary>The nearest enclosing path of <paramref name="path"/>, or <see langword="null"/> for the root.</summary>
    public static string? Parent(string path)
    {
        var cut = path.LastIndexOfAny(['.', '[', '{', '<']);
        return cut < 0 ? null : path[..cut];
    }

    private static IEnumerable<ShapeChange> CompareActivity(ActivityShape before, ActivityShape after)
    {
        var changes = CompareMembers(before.Name, "input", before.Input, after.Input)
            .Concat(CompareMembers(before.Name, "output", before.Output, after.Output));
        return before.ImplementedBy == after.ImplementedBy && before.InputType == after.InputType && before.OutputType == after.OutputType
            ? changes
            : changes.Append(new ShapeChange(before.Name, "implementation or CLR type names changed", Breaking: false));
    }

    private static IEnumerable<ShapeChange> CompareMembers(string activity, string side, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        Existing(before, after).Concat(Added(before, after)).Select(change => new ShapeChange(activity, $"{side} {change.Description}", change.Breaking));

    private static IEnumerable<(string Description, bool Breaking)> Existing(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        foreach (var (path, token) in before)
        {
            if (!after.TryGetValue(path, out var now))
            {
                yield return ($"{path} removed or renamed", true);
            }
            else if (Changed(token, now) is { } change)
            {
                yield return ($"{path} {change.Description}", change.Breaking);
            }
        }
    }

    private static IEnumerable<(string Description, bool Breaking)> Added(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        after.Where(member => !before.ContainsKey(member.Key)).Select(member =>
            ($"{member.Key} added as {member.Value}", WireShapeReader.IsRequired(member.Value) && Parent(member.Key) is { } parent && before.ContainsKey(parent)));

    private static (string Description, bool Breaking)? Changed(string before, string after)
    {
        if (before == after)
        {
            return null;
        }

        var (oldBare, newBare) = (WireShapeReader.Bare(before), WireShapeReader.Bare(after));
        if (oldBare == newBare)
        {
            return WireShapeReader.IsRequired(after) ? ("became required", true) : ("is no longer required", false);
        }

        return WireShapeReader.IsRequired(before) == WireShapeReader.IsRequired(after) && EnumChange(oldBare, newBare) is { } values
            ? values
            : ($"changed from {before} to {after}", true);
    }

    /// <summary>
    /// How an enum's wire values moved from <paramref name="before"/> to <paramref name="after"/>:
    /// losing one is breaking, since history may hold it; only gaining some is additive.
    /// <see langword="null"/> unless both tokens are enum tokens.
    /// </summary>
    public static (string Description, bool Breaking)? EnumChange(string before, string after)
    {
        if (WireShapeReader.EnumValuesOf(before) is not { } old || WireShapeReader.EnumValuesOf(after) is not { } now)
        {
            return null;
        }

        var lost = old.Except(now, StringComparer.Ordinal).ToList();
        return lost.Count > 0
            ? ($"lost enum values {string.Join(", ", lost)}", true)
            : ($"gained enum values {string.Join(", ", now.Except(old, StringComparer.Ordinal))}", false);
    }
}
