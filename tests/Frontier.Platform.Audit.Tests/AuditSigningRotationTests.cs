using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.107 end-to-end tests for the ADR-PA33 signing change: records signed inside Key Vault
/// verify locally, survive a rotation, fail closed when a version is destroyed, and — the
/// compatibility claim the whole decision rests on — a legacy <c>dev-key/v1</c> HMAC record keeps
/// verifying in the same chain as ES256 ones.
/// </summary>
public sealed class AuditSigningRotationTests
{
    private static readonly ExecutionAuditOptions NoDelay = new() { AppendMaxAttempts = 8, AppendBaseDelayMs = 0, AppendMaxDelayMs = 0 };

    [Fact]
    public async Task SignAsync_KeyVaultProfile_RecordCarriesTheVersionedKeyIdAndVerifies()
    {
        var store = new FakeAuditRecordStore();
        var vault = new FakeKeyVaultSigningClient();
        var signer = Signer(store, new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault));
        var record = AuditRecordHasherTests.Sample();

        var signed = await signer.SignAsync(record, CancellationToken.None);
        var result = await signer.VerifyAsync(record.ExecutionId, record.EngagementId, CancellationToken.None);

        Assert.Equal(FakeKeyVaultSigningClient.V1, signed.SigningKeyId);
        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
    }

    [Fact]
    public async Task VerifyAsync_RecordSignedUnderV1_StillVerifiesAfterV2BecomesCurrent()
    {
        // ADR-PA22's requirement, carried across the algorithm change: rotation re-signs nothing and
        // invalidates nothing.
        var store = new FakeAuditRecordStore();
        var vault = new FakeKeyVaultSigningClient();
        var provider = new KeyVaultKeyProvider(vault);
        var first = AuditRecordHasherTests.Sample() with { ExecutionId = "exec-1" };
        var second = AuditRecordHasherTests.Sample() with { ExecutionId = "exec-2" };

        var signedUnderV1 = await Signer(store, provider, new KeyVaultAuditSigningService(vault)).SignAsync(first, CancellationToken.None);
        vault.RotateTo(FakeKeyVaultSigningClient.V2);
        var signer = Signer(store, provider, new KeyVaultAuditSigningService(vault));
        var signedUnderV2 = await signer.SignAsync(second, CancellationToken.None);

        Assert.Equal(FakeKeyVaultSigningClient.V1, signedUnderV1.SigningKeyId);
        Assert.Equal(FakeKeyVaultSigningClient.V2, signedUnderV2.SigningKeyId);
        Assert.True((await signer.VerifyAsync("exec-1", first.EngagementId, CancellationToken.None)).SignatureValid);
        Assert.True((await signer.VerifyAsync("exec-2", second.EngagementId, CancellationToken.None)).SignatureValid);
    }

    [Fact]
    public async Task VerifyAsync_VersionDestroyed_FailsClosedAndNamesTheUnresolvedVersion()
    {
        var store = new FakeAuditRecordStore();
        var vault = new FakeKeyVaultSigningClient();
        var record = AuditRecordHasherTests.Sample();
        await Signer(store, new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault)).SignAsync(record, CancellationToken.None);

        // A fresh provider, as a later verifying process would have: nothing cached, and the version
        // is gone from the vault.
        vault.RotateTo(FakeKeyVaultSigningClient.V2);
        vault.Destroy(FakeKeyVaultSigningClient.V1);
        var verifier = Signer(store, new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault));

        var result = await verifier.VerifyAsync(record.ExecutionId, record.EngagementId, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal(FakeKeyVaultSigningClient.V1, Assert.Single(result.UnresolvedKeyIds!));

        // A missing key version is not a broken chain — hash continuity needs no key (ADR-PA22).
        Assert.True(result.ChainValid);
    }

    [Fact]
    public async Task VerifyAsync_LegacyHmacRecordBesideEs256Records_BothVerify()
    {
        // The compatibility story option (c) needed: signing_key_id is the algorithm discriminator,
        // so a chain written across the change verifies end to end with no re-signing and no
        // schema version bump.
        var store = new FakeAuditRecordStore();
        var vault = new FakeKeyVaultSigningClient();
        var devKeys = new DevKeyProvider();
        var legacy = AuditRecordHasherTests.Sample() with { ExecutionId = "exec-legacy" };
        var modern = AuditRecordHasherTests.Sample() with { ExecutionId = "exec-modern" };

        var legacyRecord = await Signer(store, devKeys, new HmacAuditSigningService(devKeys)).SignAsync(legacy, CancellationToken.None);
        var modernRecord = await Signer(store, new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault)).SignAsync(modern, CancellationToken.None);

        var verifier = Signer(store, new CompositeKeyProvider(devKeys, new KeyVaultKeyProvider(vault)), new KeyVaultAuditSigningService(vault));
        var legacyResult = await verifier.VerifyAsync("exec-legacy", legacy.EngagementId, CancellationToken.None);
        var modernResult = await verifier.VerifyAsync("exec-modern", modern.EngagementId, CancellationToken.None);

        Assert.Equal("dev-key/v1", legacyRecord.SigningKeyId);
        Assert.Equal(FakeKeyVaultSigningClient.V1, modernRecord.SigningKeyId);
        Assert.True(legacyResult.SignatureValid);
        Assert.True(modernResult.SignatureValid);
        Assert.True(modernResult.ChainValid);
    }

    [Fact]
    public async Task GovernanceAttach_SignatureFromADifferentVersionThanTheRecordNames_IsRefused()
    {
        // The governance chain hashes signing_key_id, so a rotation landing between choosing the
        // version and signing would otherwise store a record whose key id is a lie.
        var prepared = GovernanceAuditHasher.Prepare(
            GovernanceAuditSamples.Entry(),
            GovernanceAuditHasher.ComputeRecordId(GovernanceAuditSamples.Entry()),
            sequence: 1,
            GovernanceAuditSamples.Genesis,
            FakeRotatingKeyProvider.V1.KeyId);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GovernanceAuditHasher.Attach(prepared, new AuditSignature(FakeRotatingKeyProvider.V2.KeyId, "AB")));

        Assert.Contains("a rotation raced the append", exception.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    private static AuditSigner Signer(FakeAuditRecordStore store, IKeyProvider provider, IAuditSigningService signing) =>
        new(store, store, provider, signing, Options.Create(NoDelay));

    /// <summary>Resolves a key version from whichever provider knows it — a verifier that spans the algorithm change.</summary>
    private sealed class CompositeKeyProvider(IKeyProvider first, IKeyProvider second) : IKeyProvider
    {
        public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) => second.GetCurrentKeyAsync(cancellationToken);

        public async Task<SigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken) =>
            await first.GetKeyAsync(keyId, cancellationToken) ?? await second.GetKeyAsync(keyId, cancellationToken);
    }
}
