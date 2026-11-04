using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.182's one adoption step, pinned: a consumer that registers every other port but not
/// <see cref="IHostBuildStamp"/> cannot resolve either pipeline that stamps a result, and the
/// failure names the missing port. Registering it is all that is needed.
/// </summary>
public sealed class HostBuildStampRegistrationTests
{
    public static TheoryData<Type> StampingPipelines => new() { typeof(IAgentTaskActivityPipeline), typeof(IMcpToolInvocationPipeline) };

    [Theory]
    [MemberData(nameof(StampingPipelines))]
    public void Resolve_WithoutTheStamp_FailsNamingIt(Type pipeline)
    {
        using var provider = EveryPortBut(pipeline, typeof(IHostBuildStamp)).BuildServiceProvider();

        var failure = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(pipeline));

        Assert.Contains(typeof(IHostBuildStamp).FullName!, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StampingPipelines))]
    public void ValidateOnBuild_WithoutTheStamp_FailsAtStartNamingIt(Type pipeline)
    {
        var services = EveryPortBut(pipeline, typeof(IHostBuildStamp));

        var failure = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));

        Assert.Contains(failure.InnerExceptions, e => e.Message.Contains(typeof(IHostBuildStamp).FullName!, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(StampingPipelines))]
    public void Resolve_WithTheStamp_Succeeds(Type pipeline)
    {
        var services = EveryPortBut(pipeline, typeof(IHostBuildStamp))
            .AddSingleton(HostBuildStamp.FromAssembly(typeof(HostBuildStampRegistrationTests).Assembly));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService(pipeline));
    }

    /// <summary>
    /// The engine's own registrations, plus an inert stub for every interface the pipeline's
    /// constructor graph needs that the engine leaves to the consumer — except <paramref name="withheld"/>.
    /// Walking the graph, rather than listing ports by hand, keeps the test honest as constructors grow.
    /// </summary>
    private static IServiceCollection EveryPortBut(Type pipeline, Type withheld)
    {
        var services = new ServiceCollection().AddFrontierWorkflowOrchestration(Configuration());
        StubMissing(Implementation(services, pipeline), withheld, services, []);
        return services;
    }

    private static void StubMissing(Type implementation, Type withheld, IServiceCollection services, HashSet<Type> seen)
    {
        if (!seen.Add(implementation))
        {
            return;
        }

        foreach (var dependency in Constructor(implementation).GetParameters().Select(p => p.ParameterType).Where(t => t != withheld))
        {
            var registered = Registered(services, dependency);
            if (registered is not null)
            {
                StubMissing(registered, withheld, services, seen);
            }
            else if (dependency.IsInterface)
            {
                services.AddSingleton(dependency, InertPort.For(dependency));
            }
        }
    }

    private static Type Implementation(IServiceCollection services, Type service) =>
        Registered(services, service) ?? throw new InvalidOperationException($"{service} is not registered by the engine.");

    /// <summary>The implementation type the engine registered for <paramref name="service"/>; the service itself for an open-generic or instance registration.</summary>
    private static Type? Registered(IServiceCollection services, Type service)
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == service
            || (service.IsGenericType && d.ServiceType == service.GetGenericTypeDefinition()));
        return descriptor?.ImplementationType is { IsGenericTypeDefinition: false } type ? type : descriptor is null ? null : typeof(object);
    }

    private static ConstructorInfo Constructor(Type type) =>
        type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
        ?? typeof(object).GetConstructor(Type.EmptyTypes)!;

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OrchestrationOptions.ArtifactName}:SandboxMode"] = "false",
        }).Build();
}

/// <summary>A port implementation that is never called: resolution, not behaviour, is under test.</summary>
[SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "DispatchProxy.Create derives a runtime type from this class, so it cannot be sealed.")]
internal class InertPort : DispatchProxy
{
    /// <summary>An inert implementation of <paramref name="port"/>.</summary>
    internal static object For(Type port) => Create(port, typeof(InertPort));

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"{targetMethod?.Name} was called on an inert port.");
}
