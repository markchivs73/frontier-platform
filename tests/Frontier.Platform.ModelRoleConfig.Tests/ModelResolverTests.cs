namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S6.6 tests for <see cref="ModelResolver"/> (doc 08 §5): ring check, chain walk, pinning.</summary>
public sealed class ModelResolverTests
{
    private static readonly ModelEntry FleetPrimary = new()
    {
        Provider = "anthropic",
        ModelId = "claude-opus-4-8",
        InputCostPer1kGbp = 0.03m,
        OutputCostPer1kGbp = 0.15m,
        CacheReadCostPer1kGbp = 0.003m,
        ContextWindow = 200_000,
        MaxOutputTokens = 16_000,
    };

    private static readonly ModelEntry FleetFallback = new()
    {
        Provider = "anthropic",
        ModelId = "claude-fable-5",
        InputCostPer1kGbp = 0.018m,
        OutputCostPer1kGbp = 0.09m,
        CacheReadCostPer1kGbp = 0.0018m,
        ContextWindow = 200_000,
        MaxOutputTokens = 16_000,
    };

    private static RoleMapping FleetMapping(int version = 1) => new()
    {
        RoleId = "deep-reasoning",
        MappingVersion = version,
        Chain = [FleetPrimary, FleetFallback],
        Ring = RolloutRing.Fleet,
        CanaryPercent = 0,
        ChangeReason = "fleet",
        ApprovedBy = "user:mark",
        EffectiveFromUtc = DateTime.UtcNow,
    };

    private static RoleMapping CanaryMapping(int version = 2, int canaryPercent = 50, int predecessorFleetVersion = 1) => new()
    {
        RoleId = "deep-reasoning",
        MappingVersion = version,
        Chain = [FleetFallback, FleetPrimary],
        Ring = RolloutRing.Canary,
        CanaryPercent = canaryPercent,
        ChangeReason = "canary rollout",
        ApprovedBy = "user:mark",
        EffectiveFromUtc = DateTime.UtcNow,
        PredecessorFleetVersion = predecessorFleetVersion,
    };

    private static RoleMapping ShadowMapping(int version = 2, int predecessorFleetVersion = 1) => new()
    {
        RoleId = "deep-reasoning",
        MappingVersion = version,
        Chain = [FleetFallback],
        Ring = RolloutRing.Shadow,
        CanaryPercent = 0,
        ChangeReason = "shadow run",
        ApprovedBy = "user:mark",
        EffectiveFromUtc = DateTime.UtcNow,
        PredecessorFleetVersion = predecessorFleetVersion,
    };

    // ─── Fleet ring (baseline) ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NullRequest_Throws()
    {
        var resolver = new ModelResolver(new FakeRoleRegistry(FleetMapping()), new AlwaysClosedCircuitBreakerQuery());

        await Assert.ThrowsAsync<ArgumentNullException>(() => resolver.ResolveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_FleetMapping_ServesChainPrimary()
    {
        var mapping = FleetMapping();
        var resolver = new ModelResolver(new FakeRoleRegistry(mapping), new AlwaysClosedCircuitBreakerQuery());
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = "engagement-1" };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("deep-reasoning", resolved.RoleId);
        Assert.Equal(1, resolved.MappingVersion);
        Assert.Equal("anthropic", resolved.Provider);
        Assert.Equal("claude-opus-4-8", resolved.ModelId);
        Assert.Equal(0, resolved.ChainPosition);
        Assert.Equal(FleetPrimary, resolved.Entry);
    }

    [Fact]
    public async Task ResolveAsync_PinnedMappingVersion_ServesChainPrimary()
    {
        var mapping = FleetMapping();
        var resolver = new ModelResolver(new FakeRoleRegistry(mapping), new AlwaysClosedCircuitBreakerQuery());
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = "engagement-1", MappingVersion = 1 };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(1, resolved.MappingVersion);
        Assert.Equal(0, resolved.ChainPosition);
    }

    // ─── Canary ring ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CanaryRing_InCanaryEngagement_ServesCanaryMapping()
    {
        // Find an engagementId that deterministically falls in the first 50%
        var engagementId = FindEngagementInCanary(canaryPercent: 50);
        var registry = new FakeRoleRegistry(CanaryMapping(version: 2, canaryPercent: 50), FleetMapping(version: 1));
        var resolver = new ModelResolver(registry, new AlwaysClosedCircuitBreakerQuery());
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = engagementId };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(2, resolved.MappingVersion);
    }

    [Fact]
    public async Task ResolveAsync_CanaryRing_OutsideCanaryEngagement_ServesFleetFallback()
    {
        var engagementId = FindEngagementOutsideCanary(canaryPercent: 50);
        var registry = new FakeRoleRegistry(CanaryMapping(version: 2, canaryPercent: 50), FleetMapping(version: 1));
        var resolver = new ModelResolver(registry, new AlwaysClosedCircuitBreakerQuery());
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = engagementId };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(1, resolved.MappingVersion);
    }

    [Fact]
    public async Task ResolveAsync_CanaryRing_NoPredecessorFleetVersion_Throws()
    {
        var canaryNoPredecessor = new RoleMapping
        {
            RoleId = "deep-reasoning",
            MappingVersion = 2,
            Chain = [FleetFallback],
            Ring = RolloutRing.Canary,
            CanaryPercent = 1,
            ChangeReason = "test",
            ApprovedBy = "user:test",
            EffectiveFromUtc = DateTime.UtcNow,
            PredecessorFleetVersion = null,
        };
        var registry = new FakeRoleRegistry(canaryNoPredecessor);
        var resolver = new ModelResolver(registry, new AlwaysClosedCircuitBreakerQuery());

        // Find an engagement outside the 1% canary so the fallback is needed
        var engagementId = FindEngagementOutsideCanary(canaryPercent: 1);
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = engagementId };

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(request, CancellationToken.None));
    }

    // ─── Shadow ring ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShadowRing_AlwaysServesFleetPredecessor()
    {
        var registry = new FakeRoleRegistry(ShadowMapping(version: 2), FleetMapping(version: 1));
        var resolver = new ModelResolver(registry, new AlwaysClosedCircuitBreakerQuery());
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = "any-engagement" };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        // Shadow ring is never served; should always get fleet predecessor
        Assert.Equal(1, resolved.MappingVersion);
    }

    // ─── Chain walk (circuit breaker) ────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_PrimaryCircuitOpen_ServesFirstHealthyEntry()
    {
        var mapping = FleetMapping();
        var resolver = new ModelResolver(
            new FakeRoleRegistry(mapping),
            new FakeCircuitBreakerQuery(openFor: FleetPrimary.ModelId));
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = "engagement-1" };

        var resolved = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("claude-fable-5", resolved.ModelId);
        Assert.Equal(1, resolved.ChainPosition);
    }

    [Fact]
    public async Task ResolveAsync_AllCircuitsOpen_Throws()
    {
        var singleEntry = new RoleMapping
        {
            RoleId = "deep-reasoning",
            MappingVersion = 1,
            Chain = [FleetPrimary],
            Ring = RolloutRing.Fleet,
            CanaryPercent = 0,
            ChangeReason = "test",
            ApprovedBy = "user:test",
            EffectiveFromUtc = DateTime.UtcNow,
        };
        var resolver = new ModelResolver(
            new FakeRoleRegistry(singleEntry),
            new FakeCircuitBreakerQuery(openFor: FleetPrimary.ModelId));
        var request = new ResolutionRequest { RoleId = "deep-reasoning", EngagementId = "engagement-1" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(request, CancellationToken.None));
    }

    // ─── IsInCanary helper (determinism) ─────────────────────────────────────

    [Theory]
    [InlineData("eng-stable-a", 100, true)]
    [InlineData("eng-stable-a", 0, false)]
    public void IsInCanary_Deterministic_SameEngagementSameResult(string engagementId, int canaryPercent, bool expected)
    {
        var first = ModelResolver.IsInCanary(engagementId, canaryPercent);
        var second = ModelResolver.IsInCanary(engagementId, canaryPercent);

        Assert.Equal(expected, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IsInCanary_100Percent_AllEngagementsIn()
    {
        for (var i = 0; i < 20; i++)
            Assert.True(ModelResolver.IsInCanary($"eng-{i}", 100));
    }

    [Fact]
    public void IsInCanary_ZeroPercent_NoEngagementsIn()
    {
        for (var i = 0; i < 20; i++)
            Assert.False(ModelResolver.IsInCanary($"eng-{i}", 0));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string FindEngagementInCanary(int canaryPercent)
    {
        for (var i = 0; i < 1000; i++)
        {
            var id = $"eng-{i}";
            if (ModelResolver.IsInCanary(id, canaryPercent))
                return id;
        }
        throw new InvalidOperationException($"Could not find an engagement in the {canaryPercent}% canary after 1000 attempts.");
    }

    private static string FindEngagementOutsideCanary(int canaryPercent)
    {
        for (var i = 0; i < 1000; i++)
        {
            var id = $"eng-{i}";
            if (!ModelResolver.IsInCanary(id, canaryPercent))
                return id;
        }
        throw new InvalidOperationException($"Could not find an engagement outside the {canaryPercent}% canary after 1000 attempts.");
    }

    /// <summary>Returns circuit open for a specific model ID; all others closed.</summary>
    private sealed class FakeCircuitBreakerQuery(string openFor) : ICircuitBreakerQuery
    {
        public bool IsOpen(string provider, string modelId) => modelId == openFor;
    }
}
