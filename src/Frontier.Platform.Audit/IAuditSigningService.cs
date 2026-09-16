using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Audit;

/// <summary>
/// Produces a signature for a record hash (ADR-PA33). This is the seam that replaced "hand the
/// caller some key material and let it compute an HMAC": under ES256 the private key is inside Key
/// Vault and signing is an <em>operation</em> performed there, not a calculation performed here, so
/// signing and verification stopped being the same capability and became two.
///
/// <para>
/// <see cref="IKeyProvider"/> remains the verification side — it resolves the material needed to
/// check a signature, by key version, forever (ADR-PA22). This interface is the signing side, and it
/// only ever signs with the <em>current</em> version: nothing re-signs an existing record.
/// </para>
/// </summary>
public interface IAuditSigningService
{
    /// <summary>
    /// Signs <paramref name="recordHash"/> and returns the signature with the id of the key version
    /// that made it, which the caller records as the record's <c>signing_key_id</c>.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning a sentinel when the signing key cannot be reached. A record that
    /// cannot be signed must not be stored: an unsigned audit record is indistinguishable from a
    /// tampered one later, so the append fails and the evidence is simply absent (doc 05 §5).
    /// </remarks>
    Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken);

    /// <summary>
    /// The key version <see cref="SignAsync"/> would sign with now.
    /// </summary>
    /// <remarks>
    /// Needed because the governance chain hashes <c>signing_key_id</c>, so that value must be
    /// chosen before the hash to be signed exists. A rotation between this call and the signature is
    /// detected and refused rather than stored (ADR-PA33); the execution chain, which clears the
    /// field from its own hash, never needs this.
    /// </remarks>
    Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken);
}

/// <summary>A signature and the versioned key id that produced it (ADR-PA33).</summary>
[ExcludeFromCodeCoverage(Justification = "Record/POCO with assignment-only constructor")]
public sealed record AuditSignature(string KeyId, string Signature);
