namespace Frontier.Platform.Audit.Tests;

public sealed class SigningKeyCheckTests
{
    [Fact]
    public void Name_ReturnsSigningKey()
    {
        var check = new SigningKeyCheck(new FakeKeyProvider(new SigningKey("dev-key/v1", "key-material"u8.ToArray())));

        Assert.Equal("SigningKey", check.Name);
    }

    [Fact]
    public async Task CheckAsync_DevKeyProvider_ReturnsPass()
    {
        var check = new SigningKeyCheck(new DevKeyProviderForTests());

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Evaluate_KeyWithIdAndMaterial_ReturnsPass()
    {
        var result = SigningKeyCheck.Evaluate(new SigningKey("dev-key/v1", "key-material"u8.ToArray()));

        Assert.True(result.Passed);
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

    private sealed class FakeKeyProvider(SigningKey key) : IKeyProvider
    {
        public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) => Task.FromResult(key);
    }

    private sealed class DevKeyProviderForTests : IKeyProvider
    {
        public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SigningKey("dev-key/v1", "frontier-workflow-dev-signing-key"u8.ToArray()));
    }
}
