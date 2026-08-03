namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for <see cref="CachingStrategyRegistry"/> resolution and registration.</summary>
public sealed class CachingStrategyRegistryTests
{
    private readonly FakeCachingStrategy fallback = new("fallback");
    private readonly CachingStrategyRegistry registry;

    public CachingStrategyRegistryTests()
    {
        registry = new CachingStrategyRegistry(fallback);
    }

    [Fact]
    public void Constructor_NullFallback_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new CachingStrategyRegistry(null!));

    [Theory]
    [InlineData("", "claude-*")]
    [InlineData("   ", "claude-*")]
    public void Register_EmptyProvider_Throws(string provider, string modelPattern) =>
        Assert.Throws<ArgumentException>(() => registry.Register(provider, modelPattern, null, new FakeCachingStrategy("s")));

    [Theory]
    [InlineData("anthropic", "")]
    [InlineData("anthropic", "   ")]
    public void Register_EmptyModelPattern_Throws(string provider, string modelPattern) =>
        Assert.Throws<ArgumentException>(() => registry.Register(provider, modelPattern, null, new FakeCachingStrategy("s")));

    [Fact]
    public void Register_NullStrategy_Throws() =>
        Assert.Throws<ArgumentNullException>(() => registry.Register("anthropic", "claude-*", null, null!));

    [Theory]
    [InlineData("", "model")]
    [InlineData("provider", "")]
    public void Resolve_EmptyProviderOrModel_Throws(string provider, string modelId) =>
        Assert.Throws<ArgumentException>(() => registry.Resolve(provider, modelId));

    [Fact]
    public void Resolve_NoRegistrations_ReturnsFallback() =>
        Assert.Same(fallback, registry.Resolve("anthropic", "claude-3-opus"));

    [Fact]
    public void Resolve_ExactProviderModelVersionMatch_ReturnsRegisteredStrategy()
    {
        var exact = new FakeCachingStrategy("exact");
        var modelAny = new FakeCachingStrategy("model-any");
        registry.Register("anthropic", "claude-3-*", null, modelAny);
        registry.Register("anthropic", "claude-3-*", "2024-*", exact);

        var resolved = registry.Resolve("anthropic", "claude-3-opus", "2024-06");

        Assert.Same(exact, resolved);
    }

    [Fact]
    public void Resolve_NoExactVersionMatch_FallsBackToModelAnyVersion()
    {
        var modelAny = new FakeCachingStrategy("model-any");
        registry.Register("anthropic", "claude-3-*", null, modelAny);

        var resolved = registry.Resolve("anthropic", "claude-3-opus", "2024-06");

        Assert.Same(modelAny, resolved);
    }

    [Fact]
    public void Resolve_NoModelMatch_FallsBackToProviderDefault()
    {
        var providerDefault = new FakeCachingStrategy("provider-default");
        registry.Register("anthropic", "*", null, providerDefault);

        var resolved = registry.Resolve("anthropic", "claude-3-opus");

        Assert.Same(providerDefault, resolved);
    }

    [Fact]
    public void Resolve_NullModelVersion_SkipsExactMatchAndFallsBackToModelAny()
    {
        var versioned = new FakeCachingStrategy("versioned");
        var modelAny = new FakeCachingStrategy("model-any");
        registry.Register("anthropic", "claude-3-*", "2024-*", versioned);
        registry.Register("anthropic", "claude-3-*", null, modelAny);

        var resolved = registry.Resolve("anthropic", "claude-3-opus", modelVersion: null);

        Assert.Same(modelAny, resolved);
    }

    [Fact]
    public void Resolve_NoMatchAtAll_ReturnsFallback()
    {
        registry.Register("openai", "gpt-4-*", null, new FakeCachingStrategy("openai"));

        var resolved = registry.Resolve("anthropic", "claude-3-opus");

        Assert.Same(fallback, resolved);
    }

    [Fact]
    public void Resolve_ModelAnyVersionRequiresVersionPattern_FallsBackToProviderWildcard()
    {
        var versionedWildcard = new FakeCachingStrategy("versioned-wildcard");
        registry.Register("openai", "gpt-4-*", null, new FakeCachingStrategy("openai-default"));
        registry.Register("anthropic", "*", "2024-*", versionedWildcard);

        var resolved = registry.Resolve("anthropic", "claude-3-opus");

        Assert.Same(versionedWildcard, resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveStrategy_EmptyProvider_ReturnsNull(string? provider) =>
        Assert.Null(registry.ResolveStrategy(provider!));

    [Fact]
    public void ResolveStrategy_ProviderDefaultRegistered_ReturnsIt()
    {
        var providerDefault = new FakeCachingStrategy("provider-default");
        registry.Register("anthropic", "*", null, providerDefault);

        Assert.Same(providerDefault, registry.ResolveStrategy("anthropic"));
    }

    [Fact]
    public void ResolveStrategy_NoProviderDefault_ReturnsFallback() =>
        Assert.Same(fallback, registry.ResolveStrategy("anthropic"));

    [Fact]
    public void ResolveStrategy_MultipleProvidersRegistered_ReturnsMatchingProviderDefault()
    {
        var anthropicDefault = new FakeCachingStrategy("anthropic-default");
        registry.Register("openai", "*", null, new FakeCachingStrategy("openai-default"));
        registry.Register("anthropic", "*", null, anthropicDefault);

        Assert.Same(anthropicDefault, registry.ResolveStrategy("anthropic"));
    }
}
