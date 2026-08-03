using System.Globalization;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.TestSupport;

/// <summary>
/// Shared assertions for the S1.6 contract test suite (canonical-serialization skill):
/// canonical bytes are stable across cultures, match the committed golden file, and
/// round-trip through <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
/// without changing shape.
/// </summary>
internal static class ContractRoundTripAssertions
{
    private static readonly CultureInfo[] Cultures =
    [
        CultureInfo.InvariantCulture,
        new CultureInfo("en-US"),
        new CultureInfo("de-DE"),
    ];

    /// <summary>
    /// Asserts that <paramref name="value"/> serializes to byte-identical canonical bytes
    /// under every culture in <see cref="Cultures"/>, that those bytes match the committed
    /// golden file at <c>GoldenFiles/<paramref name="goldenFileName"/></c>, and that
    /// deserializing and re-serializing the result reproduces the same bytes.
    /// </summary>
    public static void AssertStableAndRoundTrips<T>(T value, string goldenFileName)
        where T : IVersionedContract
    {
        var bytes = AssertByteStableAcrossCultures(value);
        AssertMatchesGoldenFile(bytes, goldenFileName);
        AssertRoundTrips<T>(bytes);
    }

    /// <summary>Serializes <paramref name="value"/> under every culture in <see cref="Cultures"/> and asserts identical bytes, returning those bytes.</summary>
    public static byte[] AssertByteStableAcrossCultures<T>(T value)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            byte[]? expected = null;
            foreach (var culture in Cultures)
            {
                CultureInfo.CurrentCulture = culture;

                var bytes = CanonicalProfile.SerializeCanonical(value);
                expected ??= bytes;

                Assert.Equal(expected, bytes);
            }

            return expected!;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>Asserts <paramref name="bytes"/> match the committed golden file at <c>GoldenFiles/<paramref name="goldenFileName"/></c>.</summary>
    public static void AssertMatchesGoldenFile(byte[] bytes, string goldenFileName)
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "GoldenFiles", goldenFileName);
        var expected = File.ReadAllBytes(goldenPath);

        Assert.Equal(expected, bytes);
    }

    /// <summary>Asserts that deserializing <paramref name="bytes"/> and re-serializing produces the same bytes.</summary>
    public static void AssertRoundTrips<T>(byte[] bytes)
        where T : IVersionedContract
    {
        var deserialized = JsonSerializer.Deserialize<T>(bytes, CanonicalProfile.Options)!;
        var reserialized = CanonicalProfile.SerializeCanonical(deserialized);

        Assert.Equal(bytes, reserialized);
    }

    /// <summary>
    /// Asserts that <paramref name="value"/> serializes to byte-identical canonical bytes
    /// under every culture in <see cref="Cultures"/>, and that deserializing and
    /// re-serializing reproduces those bytes. For plain (non-<see cref="IVersionedContract"/>)
    /// contract records — no golden file, since these types are exercised inline as part
    /// of a versioned contract's golden file.
    /// </summary>
    public static void AssertByteStableAndRoundTrips<T>(T value)
    {
        var bytes = AssertByteStableAcrossCultures(value);

        var deserialized = JsonSerializer.Deserialize<T>(bytes, CanonicalProfile.Options)!;
        var reserialized = CanonicalProfile.SerializeCanonical(deserialized);

        Assert.Equal(bytes, reserialized);
    }
}
