using Frontier.Platform.Contracts;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>The real contract validator, over the real engines, as the composition root registers it.</summary>
internal static class TestContractValidator
{
    internal static IContractValidator Instance { get; } = new ServiceCollection()
        .AddFrontierContracts(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<IContractValidator>();
}
