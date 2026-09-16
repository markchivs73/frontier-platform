namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// Tests for <see cref="SigningKeyCheck"/> (doc 12 §6), extended at S13.107 for the ADR-PA33 probe:
/// the check now signs through <see cref="IAuditSigningService"/>, so an unreachable vault fails
/// boot rather than the first execution close.
/// </summary>
public sealed class SigningKeyCheckTests
{
    private static readonly SigningKey DevKey = new("dev-key/v1", "key-material"u8.ToArray());

    [Fact]
    public void Name_ReturnsSigningKey()
    {
        Assert.Equal("SigningKey", Check(new FakeKeyProvider(DevKey)).Name);
    }

    [Fact]
    public async Task CheckAsync_DevKeyProvider_ReturnsPass()
    {
        var result = await Check(new DevKeyProvider()).CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task CheckAsync_KeyVaultProfile_SignsAndVerifiesAgainstTheCurrentVersion()
    {
        var vault = new FakeKeyVaultSigningClient();
        var check = new SigningKeyCheck(new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_VaultUnreachable_FailsTheBootCheck()
    {
        // The S13.107 requirement: a deployed process whose vault is unreachable must refuse to
        // start, not discover it when an execution closes and its audit record cannot be written.
        var vault = new FakeKeyVaultSigningClient { Failure = new HttpRequestException("no route to host") };
        var check = new SigningKeyCheck(new KeyVaultKeyProvider(vault), new KeyVaultAuditSigningService(vault));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("could not be reached or used", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("no route to host", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_SigningServiceFails_FailsTheBootCheck()
    {
        // The provider resolves but the identity cannot sign — a missing Crypto User role assignment
        // looks exactly like this, and is the likeliest deployment mistake.
        var check = new SigningKeyCheck(new FakeKeyProvider(DevKey), new ThrowingSigningService());

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("sign-forbidden", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_Cancelled_DoesNotSwallowTheCancellation()
    {
        var check = new SigningKeyCheck(new FakeKeyProvider(DevKey), new CancellingSigningService());

        await Assert.ThrowsAsync<OperationCanceledException>(() => check.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public void Evaluate_KeyWithIdAndMaterial_ReturnsPass()
    {
        Assert.True(SigningKeyCheck.Evaluate(DevKey).Passed);
    }

    [Fact]
    public void Evaluate_EmptyKeyMaterial_ReturnsFail()
    {
        var result = SigningKeyCheck.Evaluate(new SigningKey("dev-key/v1", ReadOnlyMemory<byte>.Empty));

        Assert.False(result.Passed);
        Assert.Contains("missing an id or key material", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_EmptyKeyId_ReturnsFail()
    {
        var result = SigningKeyCheck.Evaluate(new SigningKey(string.Empty, "key-material"u8.ToArray()));

        Assert.False(result.Passed);
        Assert.Contains("missing an id or key material", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_EmptyKeyMaterial_FailsBeforeSigning()
    {
        var check = new SigningKeyCheck(new FakeKeyProvider(new SigningKey("dev-key/v1", ReadOnlyMemory<byte>.Empty)), new ThrowingSigningService());

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("missing an id or key material", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateProbe_SignedByADifferentVersionThanCurrent_Fails()
    {
        // A signing service and a key provider pointed at different keys would sign records that
        // never verify. Cheap to detect at boot, invisible until an auditor looks otherwise.
        var result = SigningKeyCheck.EvaluateProbe(new AuditSignature("dev-key/v2", "00"), DevKey);

        Assert.False(result.Passed);
        Assert.Contains("current version is 'dev-key/v1'", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateProbe_SignatureDoesNotVerify_Fails()
    {
        var result = SigningKeyCheck.EvaluateProbe(new AuditSignature(DevKey.KeyId, "ABCD"), DevKey);

        Assert.False(result.Passed);
        Assert.Contains("did not verify", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateProbe_ValidSignature_Passes()
    {
        var signature = new AuditSignature(DevKey.KeyId, AuditRecordHasher.ComputeSignature(SigningKeyCheck.ProbeRecordHash, DevKey.KeyMaterial));

        Assert.True(SigningKeyCheck.EvaluateProbe(signature, DevKey).Passed);
    }

    private static SigningKeyCheck Check(IKeyProvider provider) => new(provider, new HmacAuditSigningService(provider));

    private sealed class FakeKeyProvider(SigningKey key) : IKeyProvider
    {
        public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) => Task.FromResult(key);

        public Task<SigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult(keyId == key.KeyId ? key : null);
    }

    private sealed class ThrowingSigningService : IAuditSigningService
    {
        public Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sign-forbidden");

        public Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken) => Task.FromResult("dev-key/v1");
    }

    private sealed class CancellingSigningService : IAuditSigningService
    {
        public Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();

        public Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken) => Task.FromResult("dev-key/v1");
    }
}
