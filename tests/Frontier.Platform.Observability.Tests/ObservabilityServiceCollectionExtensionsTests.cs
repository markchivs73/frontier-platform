using Frontier.Platform.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Observability.Tests;

/// <summary>DI-wiring tests for <see cref="ObservabilityServiceCollectionExtensions"/>.</summary>
public sealed class ObservabilityServiceCollectionExtensionsTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public ObservabilityServiceCollectionExtensionsTests() =>
        _provider = new ServiceCollection().AddFrontierObservability(new ConfigurationBuilder().Build()).BuildServiceProvider();

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void AddFrontierObservability_RegistersInMemoryEmitterAsSingleton()
    {
        var first = _provider.GetRequiredService<IContextMetricsEmitter>();
        var second = _provider.GetRequiredService<IContextMetricsEmitter>();

        Assert.IsType<InMemoryContextMetricsEmitter>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierObservability_RegistersOtelPipelineCheckAsStartupCheck()
    {
        Assert.IsType<OtelPipelineCheck>(_provider.GetRequiredService<IStartupCheck>());
    }

    [Fact]
    public void AddFrontierObservability_RegistersMetricCatalogueAsSingleton()
    {
        var first = _provider.GetRequiredService<IMetricCatalogue>();
        var second = _provider.GetRequiredService<IMetricCatalogue>();

        Assert.IsType<Phase1MetricCatalogue>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierObservability_RegistersEmpiricalQueryService()
    {
        Assert.IsType<Phase1EmpiricalQueryService>(_provider.GetRequiredService<IEmpiricalQueryService>());
    }

    [Fact]
    public void AddFrontierObservability_RegistersMaturityTracker()
    {
        Assert.IsType<Phase1MaturityTracker>(_provider.GetRequiredService<IMaturityTracker>());
    }

    [Fact]
    public void AddFrontierObservability_RegistersExecutionMonitorFeed()
    {
        Assert.IsType<Phase1ExecutionMonitorFeed>(_provider.GetRequiredService<IExecutionMonitorFeed>());
    }

    [Fact]
    public void AddFrontierObservability_RegistersRecoveryFindingsRecorder()
    {
        Assert.IsType<RecoveryFindingsRecorder>(_provider.GetRequiredService<IRecoveryFindingsRecorder>());
    }

    [Fact]
    public void AddFrontierObservability_NullConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddFrontierObservability(null!));
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder().Build();
}
