namespace Frontier.Platform.Audit;

/// <summary>
/// The deployed-profile <see cref="IAuditSigningService"/> (ADR-PA33): signs the record hash inside
/// Key Vault with the key's current version, under ES256.
///
/// <para>
/// The signing identity needs <em>only</em> the sign operation on the key — doc 05 §5's sign-only
/// requirement, now literally true: nothing here reads key material, and the port it depends on has
/// no operation that could.
/// </para>
/// </summary>
internal sealed class KeyVaultAuditSigningService(IKeyVaultSigningClient client) : IAuditSigningService
{
    /// <inheritdoc />
    public async Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken)
    {
        var current = await client.GetCurrentAsync(cancellationToken);
        var digest = AuditSignatureVerifier.ComputeEs256Digest(recordHash);
        var signature = await client.SignAsync(current.KeyId, digest, cancellationToken);

        // The full versioned key identifier is what lands in signing_key_id, so verification later
        // resolves this exact version even after any number of rotations.
        return new AuditSignature(current.KeyId, Convert.ToHexString(signature));
    }

    /// <inheritdoc />
    public async Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken) =>
        (await client.GetCurrentAsync(cancellationToken)).KeyId;
}
