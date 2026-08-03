using System.Text.Json;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// Rehydrates a versioned contract from stored canonical bytes (doc 01 §5, doc 02 §6).
/// Minor schema differences deserialize directly: System.Text.Json applies the
/// declared default for any field absent from older bytes. Major schema differences
/// route through a caller-supplied adapter keyed by the stored <c>schema_version</c>,
/// applied lazily at read time — stored bytes are never rewritten.
/// </summary>
public static class ContractMigrator
{
    /// <summary>
    /// Deserializes <paramref name="storedBytes"/> as <typeparamref name="T"/>. If
    /// <paramref name="adapters"/> contains an entry for the stored <c>schema_version</c>,
    /// that adapter produces the result; otherwise <paramref name="options"/> deserializes
    /// the bytes directly (the minor-add-with-defaults path).
    /// </summary>
    public static T Rehydrate<T>(
        ReadOnlyMemory<byte> storedBytes,
        JsonSerializerOptions options,
        IReadOnlyDictionary<string, Func<JsonElement, JsonSerializerOptions, T>>? adapters = null)
        where T : IVersionedContract
    {
        ArgumentNullException.ThrowIfNull(options);

        using var document = JsonDocument.Parse(storedBytes);
        var schemaVersion = ReadSchemaVersion(document.RootElement);

        if (adapters is not null && adapters.TryGetValue(schemaVersion, out var adapter))
        {
            return adapter(document.RootElement, options);
        }

        return JsonSerializer.Deserialize<T>(document.RootElement, options)!;
    }

    /// <summary>Reads the stored <c>schema_version</c>, defaulting to <c>"1.0"</c> when the field is absent (pre-versioning bytes).</summary>
    internal static string ReadSchemaVersion(JsonElement root) =>
        root.TryGetProperty("schema_version", out var value) ? value.GetString() ?? "1.0" : "1.0";
}
