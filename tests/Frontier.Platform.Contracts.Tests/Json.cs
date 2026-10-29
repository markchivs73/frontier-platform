using System.Text.Json;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>Test helpers for JSON values and a fully legible sample contract.</summary>
internal static class Json
{
    /// <summary>A legible, closed, valid contract schema with one property and one example.</summary>
    internal const string LegibleSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "description": "An evidence summary for one finding. Not for raw evidence.",
          "type": "object",
          "properties": {
            "total": { "description": "How many items were reviewed.", "type": "integer", "minimum": 0 },
            "label": { "description": "A short name.", "type": "string", "maxLength": 10 }
          },
          "required": ["total"],
          "additionalProperties": false,
          "examples": [ { "total": 3, "label": "ok" } ]
        }
        """;

    /// <summary>Parses <paramref name="json"/> into a detached element.</summary>
    internal static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
