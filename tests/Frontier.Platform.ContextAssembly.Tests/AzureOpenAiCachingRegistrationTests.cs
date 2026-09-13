using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>ADR-PA27 test: an <c>azure-openai</c> provider resolves the OpenAI caching strategy, not the no-caching fallback.</summary>
public sealed class AzureOpenAiCachingRegistrationTests
{
    [Fact]
    public void AddFrontierContextAssembly_AzureOpenAiProvider_ResolvesTheOpenAiStrategyNotTheFallback()
    {
        using var provider = new ServiceCollection().AddFrontierContextAssembly().BuildServiceProvider();
        var registry = provider.GetRequiredService<ICachingStrategyRegistry>();

        var strategy = registry.Resolve("azure-openai", "gpt-4o");

        Assert.IsType<OpenAiCachingStrategy>(strategy);
    }
}
