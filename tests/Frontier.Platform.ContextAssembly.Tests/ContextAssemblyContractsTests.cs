using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Exercises the plain data contracts in ContextAssemblyContracts.cs: construction,
/// value equality, <c>with</c>-expressions (S3.2 design).
/// </summary>
public sealed class ContextAssemblyContractsTests
{
    [Fact]
    public void ContextPackage_WithExpressions_ProduceIndependentCopies()
    {
        var package = ContextAssemblyTestData.Package();
        var newBaseline = new BaselineTier { BaselineVersion = "2.0", Components = new[] { "new" }, Content = "new baseline" };
        var newDynamic = new DynamicTier { EngagementId = "eng-new", DynamicEpoch = 1, AssembledFromSnapshotRef = "ref", Content = "new dynamic" };
        var newRealTime = new RealTimeTier { Fetches = new List<RealTimeFetch>(), Content = "new real-time" };

        var byBaseline = package with { Baseline = newBaseline };
        var byDynamic = package with { Dynamic = newDynamic };
        var byRealTime = package with { RealTime = newRealTime };

        Assert.Equal("new baseline", byBaseline.Baseline.Content);
        Assert.Equal("new dynamic", byDynamic.Dynamic.Content);
        Assert.Equal("new real-time", byRealTime.RealTime!.Content);
        Assert.NotEqual(package, byBaseline);
    }

    [Fact]
    public void ContextPackageMetadata_ValidatesNegativeBytes()
    {
        var validMetadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: 100,
            DynamicBytes: 50,
            RealTimeBytes: 25);

        // Should not throw
        validMetadata.Validate();

        var invalidMetadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: -1,
            DynamicBytes: 50,
            RealTimeBytes: 25);

        Assert.Throws<ContractViolationException>(() => invalidMetadata.Validate());
    }

    [Theory]
    [InlineData(-1, 0, 0, "BaselineBytes")]
    [InlineData(0, -1, 0, "DynamicBytes")]
    [InlineData(0, 0, -1, "RealTimeBytes")]
    public void ContextPackageMetadata_ValidatesEachNegativeByte(int baseline, int dynamic, int realtime, string _)
    {
        var metadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: baseline,
            DynamicBytes: dynamic,
            RealTimeBytes: realtime);

        Assert.Throws<ContractViolationException>(() => metadata.Validate());
    }

    [Fact]
    public void ContextPackageMetadata_ZeroBytesIsValid()
    {
        var metadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: 0,
            DynamicBytes: 0,
            RealTimeBytes: 0);

        metadata.Validate(); // Should not throw
    }

    [Fact]
    public void ContextPackageMetadata_WithOptionalFields_ValidatesCorrectly()
    {
        var metadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: 100,
            DynamicBytes: 50,
            RealTimeBytes: 25,
            RefreshReason: "periodic",
            CacheDirectives: new[] { new ProviderCacheDirective("baseline", "anthropic", "explicit") });

        metadata.Validate(); // Should not throw
    }

    [Fact]
    public void ContextPackageMetadata_PositionalConstructor_SetsEveryProperty()
    {
        var now = DateTime.UtcNow;

        var metadata = new ContextPackageMetadata(now, 100, 50, 25, "periodic", null);

        Assert.Equal(now, metadata.AssembledAtUtc);
        Assert.Equal("periodic", metadata.RefreshReason);
    }

    [Fact]
    public void CacheHint_StorresBreakpoints()
    {
        var hint = new CacheHint
        {
            BreakpointAfterBaseline = 100,
            BreakpointAfterDynamic = 200,
            BaselineCacheKey = "baseline-key",
            DynamicCacheKey = "dynamic-key"
        };

        Assert.Equal(100, hint.BreakpointAfterBaseline);
        Assert.Equal(200, hint.BreakpointAfterDynamic);
        Assert.Equal("baseline-key", hint.BaselineCacheKey);
        Assert.Equal("dynamic-key", hint.DynamicCacheKey);
    }

    [Fact]
    public void EngagementContextEpoch_Validate_ValidRecord_DoesNotThrow()
    {
        var epoch = new EngagementContextEpoch("eng-1:ctx:e000000", "eng-1", 0, "abc123", "{}", DateTime.UtcNow);
        epoch.Validate(); // Should not throw
        Assert.Equal("1", epoch.SchemaVersion);
    }

    [Theory]
    [InlineData("", "eng-1", 0, "hash", "{}", "Id is required")]
    [InlineData("id", "", 0, "hash", "{}", "EngagementId is required")]
    [InlineData("id", "eng-1", -1, "hash", "{}", "Epoch cannot be negative")]
    [InlineData("id", "eng-1", 0, "", "{}", "ContentHash is required")]
    [InlineData("id", "eng-1", 0, "hash", "", "Content is required")]
    public void EngagementContextEpoch_Validate_InvalidField_ThrowsContractViolation(
        string id, string engagementId, int epoch, string contentHash, string content, string _)
    {
        var record = new EngagementContextEpoch(id, engagementId, epoch, contentHash, content, DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => record.Validate());
    }

    [Fact]
    public void EngagementContextPointer_Validate_ValidRecord_DoesNotThrow()
    {
        var pointer = new EngagementContextPointer("eng-1:ctx:current", "eng-1", 0, "abc123", DateTime.UtcNow);
        pointer.Validate(); // Should not throw
        Assert.Equal("1", pointer.SchemaVersion);
    }

    [Theory]
    [InlineData("", "eng-1", 0, "hash", "Id is required")]
    [InlineData("id", "", 0, "hash", "EngagementId is required")]
    [InlineData("id", "eng-1", -1, "hash", "Epoch cannot be negative")]
    [InlineData("id", "eng-1", 0, "", "ContentHash is required")]
    public void EngagementContextPointer_Validate_InvalidField_ThrowsContractViolation(
        string id, string engagementId, int epoch, string contentHash, string _)
    {
        var record = new EngagementContextPointer(id, engagementId, epoch, contentHash, DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => record.Validate());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void EngagementContextPointer_Validate_WhitespaceOnlyFields_ThrowsContractViolation(string whitespace)
    {
        var pointer = new EngagementContextPointer(whitespace, "eng-1", 0, "hash", DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => pointer.Validate());

        var pointer2 = new EngagementContextPointer("id", whitespace, 0, "hash", DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => pointer2.Validate());

        var pointer3 = new EngagementContextPointer("id", "eng-1", 0, whitespace, DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => pointer3.Validate());
    }

    [Fact]
    public void EngagementContextEpoch_Validate_LargeEpochNumber_Succeeds()
    {
        var epoch = new EngagementContextEpoch("id", "eng-1", int.MaxValue, "hash", "{}", DateTime.UtcNow);
        epoch.Validate(); // Should not throw
    }

    [Fact]
    public void EngagementContextPointer_Validate_LargeEpochNumber_Succeeds()
    {
        var pointer = new EngagementContextPointer("id", "eng-1", int.MaxValue, "hash", DateTime.UtcNow);
        pointer.Validate(); // Should not throw
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void EngagementContextEpoch_Validate_WhitespaceOnlyFields_ThrowsContractViolation(string whitespace)
    {
        var epoch = new EngagementContextEpoch(whitespace, "eng-1", 0, "hash", "{}", DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => epoch.Validate());

        var epoch2 = new EngagementContextEpoch("id", whitespace, 0, "hash", "{}", DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => epoch2.Validate());

        var epoch3 = new EngagementContextEpoch("id", "eng-1", 0, whitespace, "{}", DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => epoch3.Validate());

        var epoch4 = new EngagementContextEpoch("id", "eng-1", 0, "hash", whitespace, DateTime.UtcNow);
        Assert.Throws<ContractViolationException>(() => epoch4.Validate());
    }
}
