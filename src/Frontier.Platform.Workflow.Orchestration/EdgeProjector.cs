using System.Text.Json.Nodes;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Applies a data edge's projection (ADR-E27 D14) to the producer's output, building the consumer's
/// input: one property per <see cref="ProjectionField"/>, in declared order, taken from a JSON Pointer
/// into the output or from a literal. There is no logic. A pointer that finds nothing leaves its
/// target out, and the consumer's input contract decides whether that is allowed. Pure, so it runs
/// in the orchestrator body.
/// </summary>
internal static class EdgeProjector
{
    /// <summary>The consumer's input built from <paramref name="sourcePayload"/> by <paramref name="projection"/>.</summary>
    internal static string Apply(string sourcePayload, IReadOnlyList<ProjectionField> projection)
    {
        var source = JsonNode.Parse(sourcePayload);
        var target = new JsonObject();
        foreach (var field in projection)
        {
            if (ValueOf(field, source) is { } value)
            {
                target[field.Target] = value;
            }
        }

        return target.ToJsonString();
    }

    /// <summary>The value <paramref name="field"/> takes from <paramref name="source"/>, or null when its pointer finds nothing.</summary>
    internal static JsonNode? ValueOf(ProjectionField field, JsonNode? source) =>
        field.Literal is { } literal ? JsonNode.Parse(literal.GetRawText()) : Resolve(source, field.From!)?.DeepClone();

    /// <summary>The node at RFC 6901 <paramref name="pointer"/> in <paramref name="source"/>, or null when there is none.</summary>
    internal static JsonNode? Resolve(JsonNode? source, string pointer)
    {
        var node = source;
        foreach (var token in pointer.Split('/').Skip(1).Select(Unescape))
        {
            node = node switch
            {
                JsonObject obj => obj.TryGetPropertyValue(token, out var child) ? child : null,
                JsonArray array when int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < array.Count => array[index],
                _ => null,
            };
        }

        return node;
    }

    private static string Unescape(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}
