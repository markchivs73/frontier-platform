using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// Real P-256 key pairs, one per key id, standing in for Key Vault's key versions (ADR-PA33 tests).
///
/// <para>
/// Deliberately real cryptography rather than a stubbed signature: the point of the ES256 change is
/// that a signature made where the private key lives verifies against the public part alone, and a
/// fake that returned canned bytes would prove nothing about that. The private key never leaves this
/// class, exactly as it never leaves the vault — the tests only ever see what
/// <see cref="PublicKeyDer"/> returns.
/// </para>
/// </summary>
internal static class TestEs256Key
{
    private static readonly ConcurrentDictionary<string, ECDsa> Keys = new(StringComparer.Ordinal);

    /// <summary>The DER SubjectPublicKeyInfo of the key version <paramref name="keyId"/>.</summary>
    internal static byte[] PublicKeyDer(string keyId) => KeyFor(keyId).ExportSubjectPublicKeyInfo();

    /// <summary>A <see cref="SigningKey"/> carrying only the public part of <paramref name="keyId"/>.</summary>
    internal static SigningKey VerificationKey(string keyId) => SigningKey.ForEs256(keyId, PublicKeyDer(keyId));

    /// <summary>Signs <paramref name="digest"/> with <paramref name="keyId"/>'s private key, returning raw <c>r ‖ s</c> as Key Vault does.</summary>
    internal static byte[] SignDigest(string keyId, byte[] digest) =>
        KeyFor(keyId).SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static ECDsa KeyFor(string keyId) =>
        Keys.GetOrAdd(keyId, _ => ECDsa.Create(ECCurve.NamedCurves.nistP256));
}
