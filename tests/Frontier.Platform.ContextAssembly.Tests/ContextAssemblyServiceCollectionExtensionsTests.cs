using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>DI-wiring tests for <see cref="ContextAssemblyServiceCollectionExtensions"/>.</summary>
public sealed class ContextAssemblyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFrontierContextAssembly_NoConfigure_RegistersExpectedServices()
    {
        var services = new ServiceCollection().AddFrontierContextAssembly();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<CachingStrategyRegistry>();
        Assert.Same(registry, provider.GetRequiredService<ICachingStrategyRegistry>());
        Assert.IsType<ContextAssemblerSimple>(provider.GetRequiredService<IContextAssembler>());
        Assert.Same(NoCachingStrategy.Instance, registry.ResolveStrategy("unregistered-provider"));
    }

    [Fact]
    public void AddFrontierContextAssembly_WithConfigure_RegistersOptionsConfiguration()
    {
        var services = new ServiceCollection().AddFrontierContextAssembly(options => { });
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CachingStrategyRegistry>());
    }
}
