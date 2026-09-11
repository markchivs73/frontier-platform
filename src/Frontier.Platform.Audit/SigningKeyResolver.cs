namespace Frontier.Platform.Audit;

/// <summary>
/// Resolves the signing-key versions a stored chain was signed under (doc 05 §5 rotation:
/// "verification resolves the key <em>version</em> from <c>SigningKeyId</c>, so old records
/// verify under old key versions forever"). One <see cref="IKeyProvider.GetKeyAsync"/> call per
/// <em>distinct</em> <see cref="SignedAuditRecord.SigningKeyId"/> in the chain, not one per
/// record: a chain spans an engagement's whole life but only a handful of key versions.
/// A key id the provider cannot resolve — a destroyed version, or a forged id — is simply
/// absent from the map, which <see cref="AuditChainVerifier"/> reads as "fails closed".
/// </summary>
internal sealed class SigningKeyResolver(IKeyProvider keyProvider)
{
    /// <summary>Returns the resolvable keys of <paramref name="chain"/>, indexed by key id.</summary>
    internal async Task<IReadOnlyDictionary<string, SigningKey>> ResolveAsync(IReadOnlyList<SignedAuditRecord> chain, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<string, SigningKey>(StringComparer.Ordinal);

        foreach (var keyId in chain.Select(record => record.SigningKeyId).Distinct(StringComparer.Ordinal))
        {
            var key = await keyProvider.GetKeyAsync(keyId, cancellationToken);
            if (key is not null)
            {
                keys[keyId] = key;
            }
        }

        return keys;
    }
}
