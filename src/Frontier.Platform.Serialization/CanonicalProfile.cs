using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Frontier.Platform.Serialization;

/// <summary>
/// The one shared <see cref="JsonSerializerOptions"/> profile (doc 01 §3, ADR-C1):
/// omit-null, explicit wire names via <see cref="JsonPropertyNameAttribute"/> only
/// (no naming policy), ISO-8601-UTC-ms dates, fixed-precision decimals, smart enums
/// as canonical snake_case strings, invariant culture, strict number handling. No
/// other <see cref="JsonSerializerOptions"/> instance may be constructed anywhere in
/// the platform — definition hashing, cache hits, and audit signing all depend on
/// these bytes being byte-identical across runs, machines, and cultures.
/// </summary>
public static class CanonicalProfile
{
    /// <summary>The frozen, shared canonical serialization options.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>Serializes <paramref name="value"/> to its canonical UTF-8 bytes.</summary>
    public static byte[] SerializeCanonical<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>Computes the hex SHA-256 digest of <paramref name="value"/>'s canonical bytes.</summary>
    public static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(SerializeCanonical(value)));

    internal static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            PropertyNamingPolicy = null,
            Encoder = JavaScriptEncoder.Default,
            AllowTrailingCommas = false,
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        options.Converters.Add(new Iso8601UtcDateTimeConverter());
        options.Converters.Add(new FixedPrecisionDecimalConverter());
        options.Converters.Add(new EngagementIdConverter());
        options.Converters.Add(new SmartEnumJsonConverterFactory());
        options.MakeReadOnly();

        return options;
    }
}
