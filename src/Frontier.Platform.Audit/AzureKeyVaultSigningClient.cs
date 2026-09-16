using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Frontier.Platform.Audit;

/// <summary>
/// The Azure SDK adapter behind <see cref="IKeyVaultSigningClient"/> (ADR-PA33). It is the only
/// file in this package that names a Key Vault type, and it holds no logic beyond translating the
/// SDK's shapes into the port's — the decisions live in <see cref="KeyVaultKeyProvider"/>,
/// <see cref="KeyVaultAuditSigningService"/> and <see cref="KeyVaultKeyUri"/>, where tests reach
/// them without Azure.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "SDK adapter (doc 00 §9): pure translation onto Azure.Security.KeyVault.Keys, exercised against a real vault in a deployed environment rather than the unit-coverage gate; every behaviour it delegates to is unit-tested through IKeyVaultSigningClient.")]
internal sealed class AzureKeyVaultSigningClient : IKeyVaultSigningClient
{
    private readonly KeyClient keyClient;
    private readonly TokenCredential credential;
    private readonly string keyName;

    /// <summary>Builds the clients for the key at <paramref name="keyUri"/>, authenticating with <paramref name="credential"/> (ADR-SEC3).</summary>
    internal AzureKeyVaultSigningClient(string keyUri, TokenCredential credential)
    {
        this.credential = credential;
        keyName = KeyVaultKeyUri.KeyName(keyUri);
        keyClient = new KeyClient(KeyVaultKeyUri.VaultUri(keyUri), credential);
    }

    /// <inheritdoc />
    public async Task<KeyVaultPublicKey> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var key = await keyClient.GetKeyAsync(keyName, cancellationToken: cancellationToken);

        return ToPublicKey(key.Value);
    }

    /// <inheritdoc />
    public async Task<KeyVaultPublicKey?> GetByIdAsync(string keyId, CancellationToken cancellationToken)
    {
        try
        {
            var version = KeyVersion(keyId);
            var key = await keyClient.GetKeyAsync(keyName, version, cancellationToken);

            return ToPublicKey(key.Value);
        }
        catch (RequestFailedException failure) when (failure.Status is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Forbidden)
        {
            // Destroyed, purged, or not readable by this identity. Either way this process cannot
            // vouch for the version, so it resolves to null and the record fails closed (ADR-PA22).
            return null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> SignAsync(string keyId, byte[] digest, CancellationToken cancellationToken)
    {
        var cryptographyClient = new CryptographyClient(new Uri(keyId, UriKind.Absolute), credential);
        var result = await cryptographyClient.SignAsync(SignatureAlgorithm.ES256, digest, cancellationToken);

        return result.Signature;
    }

    /// <summary>The version segment of a full key identifier.</summary>
    private static string KeyVersion(string keyId) => new Uri(keyId, UriKind.Absolute).Segments[^1].Trim('/');

    /// <summary>Exports the key version's public part as a DER SubjectPublicKeyInfo.</summary>
    private static KeyVaultPublicKey ToPublicKey(KeyVaultKey key)
    {
        using var ecdsa = key.Key.ToECDsa(includePrivateParameters: false);

        return new KeyVaultPublicKey(key.Id.ToString(), ecdsa.ExportSubjectPublicKeyInfo());
    }
}
