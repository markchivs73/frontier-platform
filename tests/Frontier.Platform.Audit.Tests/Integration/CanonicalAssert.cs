using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// Canonical-byte equality assertion for round-trip integration tests. Compiler-generated
/// record equality compares <c>IReadOnlyList</c> members by <em>reference</em>, so a
/// whole-record <c>Assert.Equal</c> against a deserialized copy can never pass — these
/// assertions sat unexecuted (the suites pointed at the retired HTTPS emulator) until
/// S9.23 made them runnable, which exposed it. The platform's identity definition is the
/// record's canonical bytes (doc 01 §3; canonical-serialization skill: hashing, cache
/// identity, and audit signing all key on them), so asserting on those is the stronger
/// check — it also normalizes decimal wire scale via
/// <see cref="FixedPrecisionDecimalConverter"/> (in-memory <c>93.3m</c> and round-tripped
/// <c>93.3000m</c> both serialize to <c>"93.3000"</c>).
/// </summary>
internal static class CanonicalAssert
{
    /// <summary>Asserts <paramref name="actual"/> has byte-identical canonical form to <paramref name="expected"/>.</summary>
    internal static void Equal<T>(T expected, T actual) =>
        Assert.Equal(
            JsonSerializer.Serialize(expected, CanonicalProfile.Options),
            JsonSerializer.Serialize(actual, CanonicalProfile.Options));
}
