namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.66 tests for <see cref="SigningKeyResolver"/> (doc 05 §5 key-version resolution).</summary>
public sealed class SigningKeyResolverTests
{
    [Fact]
    public async Task ResolveAsync_ChainSpanningTwoKeys_ReturnsBothKeysByKeyId()
    {
        var provider = new FakeRotatingKeyProvider();
        var chain = Chain(FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V2);

        var keys = await new SigningKeyResolver(provider).ResolveAsync(chain, CancellationToken.None);

        Assert.Equal(2, keys.Count);
        Assert.Equal(FakeRotatingKeyProvider.V1.KeyId, keys[FakeRotatingKeyProvider.V1.KeyId].KeyId);
        Assert.Equal(FakeRotatingKeyProvider.V2.KeyId, keys[FakeRotatingKeyProvider.V2.KeyId].KeyId);
    }

    [Fact]
    public async Task ResolveAsync_LongChainOverTwoRotations_CallsProviderOncePerDistinctKeyId()
    {
        // The defect's cost guard: a 500-record chain across two rotations is three key versions,
        // so it must cost three provider calls, not 500.
        var provider = new FakeRotatingKeyProvider();
        provider.RotateTo(FakeRotatingKeyProvider.V3);
        var keyOrder = new[] { FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V2, FakeRotatingKeyProvider.V3 };
        var chain = Chain([.. Enumerable.Range(0, 500).Select(index => keyOrder[index / 167])]);

        var keys = await new SigningKeyResolver(provider).ResolveAsync(chain, CancellationToken.None);

        Assert.Equal(3, provider.GetKeyCallCount);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public async Task ResolveAsync_RepeatedKeyId_CallsProviderOnce()
    {
        var provider = new FakeRotatingKeyProvider();
        var chain = Chain(FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V1);

        var keys = await new SigningKeyResolver(provider).ResolveAsync(chain, CancellationToken.None);

        Assert.Equal(1, provider.GetKeyCallCount);
        Assert.Single(keys);
    }

    [Fact]
    public async Task ResolveAsync_UnresolvableKeyId_OmitsItFromTheMap()
    {
        var provider = new FakeRotatingKeyProvider();
        provider.Forget(FakeRotatingKeyProvider.V2.KeyId);
        var chain = Chain(FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V2);

        var keys = await new SigningKeyResolver(provider).ResolveAsync(chain, CancellationToken.None);

        Assert.True(keys.ContainsKey(FakeRotatingKeyProvider.V1.KeyId));
        Assert.False(keys.ContainsKey(FakeRotatingKeyProvider.V2.KeyId));
    }

    [Fact]
    public async Task ResolveAsync_EmptyChain_ReturnsEmptyMapWithoutCallingTheProvider()
    {
        var provider = new FakeRotatingKeyProvider();

        var keys = await new SigningKeyResolver(provider).ResolveAsync([], CancellationToken.None);

        Assert.Empty(keys);
        Assert.Equal(0, provider.GetKeyCallCount);
    }

    /// <summary>A chain whose n-th record is signed with <paramref name="keys"/>[n], correctly linked from genesis.</summary>
    private static SignedAuditRecord[] Chain(params SigningKey[] keys)
    {
        var records = new List<SignedAuditRecord>();
        var previousHash = AuditRecordHasher.ComputeGenesisHash("eng-1");

        for (var index = 0; index < keys.Length; index++)
        {
            var unsigned = AuditRecordHasherTests.Sample() with { ExecutionId = $"eng-1::wf-{index}", WorkflowId = $"wf-{index}" };
            var record = AuditChainVerifierTests.Sign(unsigned, previousHash, keys[index]);
            records.Add(record);
            previousHash = record.RecordHash;
        }

        return [.. records];
    }
}
