using System.Security.Cryptography;
using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The content hash of a registry contract (ADR-E27 D4): <c>sha256:</c> plus the lower-case hex
/// SHA-256 of the RFC 8785 (JCS) form of <c>{"rules": [...], "schema": {...}}</c>. Each rule is
/// <c>{"field_path"?, "message", "rule"}</c>, with <c>field_path</c> omitted when null, kept in
/// registration order. Annotations sit inside the schema, so editing one changes the hash
/// (ADR-E27 Q2). The prefix and casing match the definition hash.
/// </summary>
public static class ContractHash
{
    /// <summary>The hash of <paramref name="schema"/> and <paramref name="rules"/>.</summary>
    /// <exception cref="ArgumentException">The schema contains a number that is not finite.</exception>
    public static string Compute(JsonElement schema, IReadOnlyList<ContractRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var canonical = JsonCanonicalizer.Canonicalize(HashInput(schema, rules));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    /// <summary>The JSON text the hash is taken over, before canonicalisation.</summary>
    internal static byte[] HashInput(JsonElement schema, IReadOnlyList<ContractRule> rules)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rules");
            foreach (var rule in rules)
            {
                WriteRule(writer, rule);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("schema");
            schema.WriteTo(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteRule(Utf8JsonWriter writer, ContractRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("rule", rule.Rule);
        writer.WriteString("message", rule.Message);
        if (rule.FieldPath is not null)
        {
            writer.WriteString("field_path", rule.FieldPath);
        }

        writer.WriteEndObject();
    }
}
