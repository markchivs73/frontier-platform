extern alias identity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// The on-demand live proof of the ADR-PA33 signing path against a <em>real</em> Azure Key Vault.
///
/// <para>
/// Everything else covering S13.107 runs against <see cref="FakeKeyVaultSigningClient"/>, which is
/// the right layer for the decisions but proves nothing about the SDK adapter, the credential, the
/// wire encoding of a Key Vault signature, or the shape of the key identifier the service actually
/// returns. Those are exactly the things that fail first in a deployment, so they are proved here
/// against the vault itself and nowhere else.
/// </para>
///
/// <para>
/// Carries <c>Integration</c> plus <c>RequiresAzureKeyVault</c>, so it is excluded from both CI jobs
/// (the unit gate filters <c>Category!=Integration</c>; the integration job filters
/// <c>Category!=RequiresAzureKeyVault</c>) — the same double-trait the model-gated suites use. Run it
/// on demand per the Audit package README. Every vault-dependent test skips with a clear message
/// when <c>AuditSigning:KeyIdentifier</c> is unset, so a machine with no Azure stays green.
/// </para>
///
/// <para>
/// It creates nothing and modifies nothing: the only vault operations are reads of a key version's
/// <em>public</em> part and sign calls. No key material, token or credential is printed, logged or
/// asserted on — a signature is evidence, key material is not.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "RequiresAzureKeyVault")]
public sealed class KeyVaultLiveSigningTests
{
    [KeyVaultFact]
    public async Task SignAsync_SignsARealRecordInTheVaultAndItVerifiesLocally()
    {
        // The round trip ADR-PA33 rests on: the signature is made where the private key lives, and
        // checking it needs only the version's public part — no vault call, no secret, no grant.
        var vault = CreateVault();
        var provider = new KeyVaultKeyProvider(vault);
        var signing = new KeyVaultAuditSigningService(vault);

        var engagementId = LiveEngagementId();
        var record = LiveRecord(engagementId, "exec-live-round-trip");
        var previousRecordHash = AuditRecordHasher.ComputeGenesisHash(engagementId);
        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);

        var signature = await signing.SignAsync(recordHash, CancellationToken.None);

        var key = await provider.GetKeyAsync(signature.KeyId, CancellationToken.None);
        Assert.NotNull(key);
        Assert.Equal(SigningAlgorithm.Es256, key.Algorithm);
        Assert.True(AuditSignatureVerifier.Verify(recordHash, signature.Signature, key));

        // Verified again from the provider's cache, which is the state an auditor walking a long
        // chain is in after the first record: still local, still true.
        var cached = await provider.GetKeyAsync(signature.KeyId, CancellationToken.None);
        Assert.True(AuditSignatureVerifier.Verify(recordHash, signature.Signature, cached!));

        // The signature is over this record and no other: a different record's hash fails under the
        // same key, so the verification above is not vacuously true.
        var otherHash = AuditRecordHasher.ComputeRecordHash(record with { WorkflowId = "wf-other" }, previousRecordHash);
        Assert.False(AuditSignatureVerifier.Verify(otherHash, signature.Signature, key));

        // And it verifies as a stored record, through the shape the chain verifier reads.
        var signed = AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature.Signature, signature.KeyId);
        Assert.True(AuditChainVerifier.IsSignatureValid(signed, Keys(key)));
    }

    [KeyVaultFact]
    public async Task SignAsync_CarriesTheVersionedKeyIdentifierAndVerificationResolvesThatExactVersion()
    {
        // ADR-PA22 through a real vault: configuration names the key without a version, the
        // signature names the version that made it, and that exact version is what verifies.
        var vault = CreateVault();
        var provider = new KeyVaultKeyProvider(vault);
        var configuredKeyIdentifier = LiveKeyVault.KeyIdentifier!.TrimEnd('/');

        var recordHash = AuditRecordHasher.ComputeGenesisHash(LiveEngagementId());
        var signature = await new KeyVaultAuditSigningService(vault).SignAsync(recordHash, CancellationToken.None);

        Assert.StartsWith($"{configuredKeyIdentifier}/", signature.KeyId, StringComparison.Ordinal);
        Assert.NotEqual(configuredKeyIdentifier, signature.KeyId);
        Assert.NotEmpty(signature.KeyId.Split('/')[^1]);

        var key = await provider.GetKeyAsync(signature.KeyId, CancellationToken.None);
        Assert.NotNull(key);
        Assert.Equal(signature.KeyId, key.KeyId);
        Assert.True(AuditSignatureVerifier.Verify(recordHash, signature.Signature, key));

        // A version this vault never issued resolves to null rather than falling back to the current
        // key — the fail-closed half of ADR-PA22, proved against the real service's 404.
        var neverIssued = $"{configuredKeyIdentifier}/{new string('0', 32)}";
        Assert.Null(await provider.GetKeyAsync(neverIssued, CancellationToken.None));
    }

    [KeyVaultFact]
    public async Task Verify_LegacyHmacAndRealEs256RecordsVerifySideBySideInOneChain()
    {
        // ADR-PA33 decision 3: signing_key_id is the algorithm discriminator, so no record ever
        // written needed re-signing. A dev-key HMAC record and a vault ES256 record share one chain.
        var vault = CreateVault();
        var provider = new KeyVaultKeyProvider(vault);
        var engagementId = LiveEngagementId();
        var genesis = AuditRecordHasher.ComputeGenesisHash(engagementId);

        var devKey = await new DevKeyProvider().GetCurrentKeyAsync(CancellationToken.None);
        var legacyRecord = LiveRecord(engagementId, "exec-legacy-hmac");
        var legacyHash = AuditRecordHasher.ComputeRecordHash(legacyRecord, genesis);
        var legacySigned = AuditRecordHasher.ToSignedShape(
            legacyRecord, genesis, legacyHash, AuditRecordHasher.ComputeSignature(legacyHash, devKey.KeyMaterial), devKey.KeyId);

        var liveRecord = LiveRecord(engagementId, "exec-live-es256");
        var liveHash = AuditRecordHasher.ComputeRecordHash(liveRecord, legacyHash);
        var liveSignature = await new KeyVaultAuditSigningService(vault).SignAsync(liveHash, CancellationToken.None);
        var liveSigned = AuditRecordHasher.ToSignedShape(liveRecord, legacyHash, liveHash, liveSignature.Signature, liveSignature.KeyId);

        var es256Key = await provider.GetKeyAsync(liveSignature.KeyId, CancellationToken.None);
        Assert.NotNull(es256Key);
        Assert.Equal(SigningAlgorithm.HmacSha256, devKey.Algorithm);
        Assert.Equal(SigningAlgorithm.Es256, es256Key.Algorithm);

        SignedAuditRecord[] chain = [legacySigned, liveSigned];
        var keys = Keys(devKey, es256Key);

        var live = AuditChainVerifier.Verify(chain, liveRecord.ExecutionId, engagementId, keys);
        Assert.True(live.SignatureValid);
        Assert.True(live.ChainValid);
        Assert.Null(live.BrokenLinkAt);
        Assert.Null(live.UnresolvedKeyIds);
        Assert.Equal(liveSignature.KeyId, live.VerifiedAgainstKeyId);

        var legacy = AuditChainVerifier.Verify(chain, legacyRecord.ExecutionId, engagementId, keys);
        Assert.True(legacy.SignatureValid);
        Assert.True(legacy.ChainValid);
        Assert.Equal(devKey.KeyId, legacy.VerifiedAgainstKeyId);
    }

    [KeyVaultFact]
    public async Task SigningKeyCheck_ProbesTheRealVaultAndPasses()
    {
        // The boot probe makes a genuine sign call and verifies the result, so an unreachable vault,
        // a missing key or an unassigned Key Vault Crypto User role fails boot rather than surfacing
        // at the first execution close. This asserts the passing side against the real thing.
        var vault = CreateVault();
        var check = new SigningKeyCheck(new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed, result.FailureReason);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task SigningProfileCheck_RefusesTheDevKeyOutsideTheLocalProfile()
    {
        // No vault needed, so this one never skips: it is the guard that makes the whole ADR matter,
        // and it must hold on a machine with no Azure at all.
        var deployed = Options.Create(new CosmosOptions { Endpoint = "https://frontier-prod.documents.azure.com:443/", Key = "unused", Database = "frontier-workflow" });

        var devKeyOutsideLocal = await new SigningProfileCheck(Options.Create(new AuditSigningOptions()), deployed)
            .CheckAsync(CancellationToken.None);

        Assert.False(devKeyOutsideLocal.Passed);
        Assert.Contains("AuditSigning:KeyIdentifier", devKeyOutsideLocal.FailureReason, StringComparison.Ordinal);

        // The same deployed profile is satisfied once a vault key is configured.
        var withVaultKey = Options.Create(new AuditSigningOptions { KeyIdentifier = "https://example-kv.vault.azure.net/keys/audit" });

        Assert.True((await new SigningProfileCheck(withVaultKey, deployed).CheckAsync(CancellationToken.None)).Passed);
    }

    /// <summary>The live vault adapter, authenticated exactly as a deployment is (ADR-SEC3).</summary>
    private static AzureKeyVaultSigningClient CreateVault() =>
        new AzureKeyVaultSigningClient(LiveKeyVault.KeyIdentifier!, new identity::Azure.Identity.DefaultAzureCredential());

    /// <summary>An engagement id unique to this run, so nothing collides and nothing is reused.</summary>
    private static string LiveEngagementId() => $"eng-live-{Guid.NewGuid():N}";

    /// <summary>A well-formed record for <paramref name="engagementId"/>, off the shared sample.</summary>
    private static AuditRecord LiveRecord(string engagementId, string executionId) =>
        AuditContractSamples.AuditRecord() with { EngagementId = engagementId, ExecutionId = executionId };

    /// <summary>The resolved verification keys, indexed as the chain verifier expects.</summary>
    private static Dictionary<string, SigningKey> Keys(params SigningKey[] keys) =>
        keys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips its test — with a message naming exactly what to set —
/// when no Key Vault key identifier is configured. Configuration is read from the environment or
/// user-secrets and never from a committed value, so the suite stays green on a machine with no
/// Azure instead of failing for a reason that is not a defect.
/// </summary>
internal sealed class KeyVaultFactAttribute : FactAttribute
{
    public KeyVaultFactAttribute()
    {
        if (LiveKeyVault.KeyIdentifier is null)
        {
            Skip = "Live Key Vault gate: set AuditSigning:KeyIdentifier to a versionless Key Vault key " +
                "identifier (env 'AuditSigning__KeyIdentifier', or user-secrets on this test project) " +
                "and hold Key Vault Crypto User on that key. Skipped because it is unset.";
        }
    }
}

/// <summary>
/// Where the live gate's vault location comes from: the same <c>AuditSigning:KeyIdentifier</c> key
/// <see cref="AuditServiceCollectionExtensions.AddSigningProfile"/> reads, from the environment or
/// user-secrets. A location, never a credential — the vault is reached with
/// <c>DefaultAzureCredential</c> (ADR-SEC3, ADR-SEC5).
/// </summary>
internal static class LiveKeyVault
{
    /// <summary>The configured versionless key identifier, or <see langword="null"/> when unset.</summary>
    internal static string? KeyIdentifier { get; } = Resolve();

    private static string? Resolve()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(LiveKeyVault).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new AuditSigningOptions
        {
            KeyIdentifier = configuration.GetSection(AuditSigningOptions.SectionName)["KeyIdentifier"],
        };

        return options.UsesKeyVault ? options.KeyIdentifier : null;
    }
}
