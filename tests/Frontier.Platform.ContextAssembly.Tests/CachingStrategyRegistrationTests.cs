using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests;

public sealed class CachingStrategyRegistrationTests
{
    [Fact]
    public void Registry_RegistersOpenAiStrategy()
    {
        var registry = new CachingStrategyRegistry(NoCachingStrategy.Instance);
        registry.Register("anthropic", "claude-*", versionPattern: null, new AnthropicCachingStrategy());
        registry.Register("openai", modelPattern: "*", versionPattern: null, new OpenAiCachingStrategy());

        var strategy = registry.Resolve("openai", "gpt-4", null);

        Assert.NotNull(strategy);
        Assert.IsType<OpenAiCachingStrategy>(strategy);
        Assert.Equal("openai", strategy.ProviderName);
    }

    [Fact]
    public void Registry_AnthropicStillWorks()
    {
        var registry = new CachingStrategyRegistry(NoCachingStrategy.Instance);
        registry.Register("anthropic", "claude-*", versionPattern: null, new AnthropicCachingStrategy());
        registry.Register("openai", modelPattern: "*", versionPattern: null, new OpenAiCachingStrategy());

        var strategy = registry.Resolve("anthropic", "claude-opus", null);

        Assert.NotNull(strategy);
        Assert.IsType<AnthropicCachingStrategy>(strategy);
        Assert.Equal("anthropic", strategy.ProviderName);
    }

    [Fact]
    public void Registry_FallbackToDefaultWhenUnregistered()
    {
        var registry = new CachingStrategyRegistry(NoCachingStrategy.Instance);
        registry.Register("anthropic", "claude-*", versionPattern: null, new AnthropicCachingStrategy());

        var strategy = registry.Resolve("unknown-provider", "unknown-model", null);

        Assert.NotNull(strategy);
        Assert.IsType<NoCachingStrategy>(strategy);
    }
}
