using System.Text.Json;

namespace Frontier.Platform.Contracts;

/// <summary>How deeply a JSON value nests, counted as <see cref="JsonDocumentOptions.MaxDepth"/> counts it.</summary>
internal static class PayloadDepth
{
    /// <summary>The nesting depth of <paramref name="value"/>: 0 for a scalar, 1 for a flat object or array.</summary>
    internal static int Of(JsonElement value)
    {
        var deepest = 0;
        var pending = new Stack<(JsonElement Value, int Depth)>();
        pending.Push((value, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            var children = Children(current);
            if (children is not null)
            {
                deepest = Math.Max(deepest, depth + 1);
                foreach (var child in children)
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        return deepest;
    }

    private static IEnumerable<JsonElement>? Children(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(property => property.Value),
        JsonValueKind.Array => value.EnumerateArray(),
        _ => null,
    };
}
