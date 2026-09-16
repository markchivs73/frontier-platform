namespace Frontier.Platform.Audit;

/// <summary>
/// The local-profile <see cref="IAuditSigningService"/> (ADR-PA33): signs with the current
/// <see cref="IKeyProvider"/> key's material, which is the pre-ADR-PA33 behaviour preserved exactly
/// so a developer's laptop needs no Key Vault and no Azure sign-in to run the audit chain.
///
/// <para>
/// It is registered only when the deployment resolves to the local profile, and
/// <see cref="SigningProfileCheck"/> refuses to boot if that inference is ever wrong. Signing real
/// evidence with a key that is committed to the repository is precisely the S13.107 defect.
/// </para>
/// </summary>
internal sealed class HmacAuditSigningService(IKeyProvider keyProvider) : IAuditSigningService
{
    /// <inheritdoc />
    public async Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken)
    {
        var key = await keyProvider.GetCurrentKeyAsync(cancellationToken);

        return new AuditSignature(key.KeyId, AuditSignatureVerifier.SignHmac(recordHash, key.KeyMaterial));
    }

    /// <inheritdoc />
    public async Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken) =>
        (await keyProvider.GetCurrentKeyAsync(cancellationToken)).KeyId;
}
