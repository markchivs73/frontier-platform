namespace Frontier.Platform.Serialization;

/// <summary>
/// Boot check (doc 12 §6): serializes <see cref="CanonicalProfileCheckFixture"/>
/// through <see cref="CanonicalProfile"/> and compares its SHA-256 digest against
/// <see cref="ExpectedFixtureHashHex"/>, the committed known-good. A mismatch means the
/// shared profile (naming, ordering, omit-null, converters) has drifted from what
/// definition hashing, cache keys, and audit signing were built against — the cheapest
/// insurance in the platform.
/// </summary>
internal sealed class CanonicalProfileCheck : IStartupCheck
{
    /// <summary>The committed SHA-256 digest of <see cref="CanonicalProfileCheckFixture"/>'s canonical bytes.</summary>
    internal const string ExpectedFixtureHashHex = "D3F699DF12B524620FC93869B7BDE1EAC5C4885975AF8A694D133F86B801AEEC";

    /// <inheritdoc />
    public string Name => "CanonicalProfile";

    /// <inheritdoc />
    public Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(ExpectedFixtureHashHex));

    /// <summary>Computes the fixture's current canonical hash and compares it against <paramref name="expectedHashHex"/>.</summary>
    internal static StartupCheckResult Evaluate(string expectedHashHex)
    {
        var actual = CanonicalProfile.Hash(new CanonicalProfileCheckFixture());

        return string.Equals(actual, expectedHashHex, StringComparison.Ordinal)
            ? StartupCheckResult.Pass()
            : StartupCheckResult.Fail($"Canonical serialization drift: expected SHA-256 {expectedHashHex}, computed {actual} (doc 12 §6).");
    }
}
