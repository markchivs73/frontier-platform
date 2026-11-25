using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Consumer S13.146 / S13.147 (ADR-PA37 and ADR-PA43, amended 2026-11-25): a running dispatcher re-reads its governed
/// settings at each generation boundary, through the resolve activity and never in the body, and each value moves with
/// its version.
/// </summary>
public sealed class DispatcherGenerationSettingsTests
{
    private static readonly ContractBudgets Old = new() { MaxRuleCost = 500_000, MaxRuleIterations = 50_000, MaxPayloadDepth = 32, MaxViolations = 10 };
    private static readonly ContractBudgets New = Old with { MaxViolations = 20 };

    private static GraphOrchestratorInput Pinned() => DispatcherFixtures.Input() with
    {
        ContractBudgets = Old,
        ContractBudgetsVersion = 3,
        DispatcherRolloverThreshold = 50,
        DispatcherSettingsVersion = 2,
    };

    [Fact]
    public void BuildNextGenerationInput_CarriesForward_WhenTheResultNamesNoSettings()
    {
        var next = DispatcherSteps.BuildNextGenerationInput(Pinned(), new ResolveDispatcherVersionResult());

        Assert.Equal((Old, 3, 50, 2), (next.ContractBudgets, next.ContractBudgetsVersion, next.DispatcherRolloverThreshold, next.DispatcherSettingsVersion));
    }

    [Fact]
    public void BuildNextGenerationInput_TakesTheResolvedBudgetsThresholdAndTheirVersions()
    {
        var next = DispatcherSteps.BuildNextGenerationInput(Pinned(), new ResolveDispatcherVersionResult
        {
            ContractBudgets = New, ContractBudgetsVersion = 4, DispatcherRolloverThreshold = 250, DispatcherSettingsVersion = 5,
        });

        Assert.Equal((New, 4, 250, 5), (next.ContractBudgets, next.ContractBudgetsVersion, next.DispatcherRolloverThreshold, next.DispatcherSettingsVersion));
    }

    /// <summary>A value moves with its version: new budgets or a new threshold with no version clear the version, never keep the old one.</summary>
    [Fact]
    public void BuildNextGenerationInput_NewValueWithANullVersion_ClearsTheVersion()
    {
        var next = DispatcherSteps.BuildNextGenerationInput(Pinned(), new ResolveDispatcherVersionResult { ContractBudgets = New, DispatcherRolloverThreshold = 250 });

        Assert.Equal((New, (int?)null, 250, (int?)null), (next.ContractBudgets, next.ContractBudgetsVersion, next.DispatcherRolloverThreshold, next.DispatcherSettingsVersion));
    }

    /// <summary>A version with no value is not taken: the version only ever names the value it came with.</summary>
    [Fact]
    public void BuildNextGenerationInput_VersionWithoutItsValue_KeepsThePair()
    {
        var next = DispatcherSteps.BuildNextGenerationInput(Pinned(), new ResolveDispatcherVersionResult { ContractBudgetsVersion = 9, DispatcherSettingsVersion = 9 });

        Assert.Equal((3, 2), (next.ContractBudgetsVersion, next.DispatcherSettingsVersion));
    }

    [Fact]
    public void BuildChildInput_CarriesTheBudgetsAndTheirVersion_NotTheSettingsVersion()
    {
        var child = DispatcherSteps.BuildChildInput(new FakeTaskOrchestrationContext(), Pinned(), DispatcherFixtures.Item("wi-1"));

        Assert.Equal((Old, 3), (child.ContractBudgets, child.ContractBudgetsVersion));
        Assert.Null(child.DispatcherSettingsVersion);
        Assert.Null(child.DispatcherRolloverThreshold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RolloverThresholdFor_AResolvedValueOfZeroOrLess_FallsBackToTheDefault(int resolved)
    {
        var next = DispatcherSteps.BuildNextGenerationInput(Pinned(), new ResolveDispatcherVersionResult { DispatcherRolloverThreshold = resolved });

        Assert.Equal(DispatcherOrchestrator.DefaultContinueAsNewThreshold, DispatcherOrchestrator.RolloverThresholdFor(next));
    }

    /// <summary>
    /// Brief §4.4's pre-change replay case: a rollover result recorded before these members existed (exactly
    /// <c>{"definition":…}</c>, or <c>{}</c>) continues-as-new with byte-identical input to what the old code built.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecordedPreChangeResult_ContinuesAsNewWithTheOldInputBytes(bool withSuccessor)
    {
        var successor = OrchestrationFixtures.ThreeArtifactChain(ExecutionMode.Dispatcher) with { DefinitionVersion = 12 };
        var recordedJson = withSuccessor ? $"{{\"definition\":{JsonSerializer.Serialize(successor, CanonicalProfile.Options)}}}" : "{}";
        var recorded = JsonSerializer.Deserialize<ResolveDispatcherVersionResult>(recordedJson, CanonicalProfile.Options)!;
        var input = DispatcherFixtures.Input() with { ContractBudgets = Old, DispatcherRolloverThreshold = 7 };
        var oldCode = new GraphOrchestratorInput
        {
            Definition = recorded.Definition ?? input.Definition,
            EngagementId = input.EngagementId,
            InitiatedBy = input.InitiatedBy,
            RunId = input.RunId,
            DynamicContextEpoch = input.DynamicContextEpoch,
            DynamicContextHash = input.DynamicContextHash,
            PinModelRoles = input.PinModelRoles,
            DispatcherRolloverThreshold = input.DispatcherRolloverThreshold,
            ContractBudgets = input.ContractBudgets,
        };

        var next = DispatcherSteps.BuildNextGenerationInput(input, recorded);

        Assert.Equal(CanonicalProfile.SerializeCanonical(oldCode), CanonicalProfile.SerializeCanonical(next));
    }

    /// <summary>A result written by the new code still deserializes where only the definition is read, and carries forward.</summary>
    [Fact]
    public void ResultWithTheNewMembers_RoundTripsAndOmitsNullMembers()
    {
        var full = new ResolveDispatcherVersionResult { ContractBudgets = New, ContractBudgetsVersion = 4, DispatcherRolloverThreshold = 250, DispatcherSettingsVersion = 5 };

        var bytes = CanonicalProfile.SerializeCanonical(full);

        Assert.Equal(full, JsonSerializer.Deserialize<ResolveDispatcherVersionResult>(bytes, CanonicalProfile.Options));
        Assert.EndsWith("\"contract_budgets_version\":4,\"dispatcher_rollover_threshold\":250,\"dispatcher_settings_version\":5}", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal("{}", System.Text.Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(new ResolveDispatcherVersionResult())));
    }

    [Fact]
    public async Task Activity_FillsTheResultFromBothPorts()
    {
        var settings = new FakeSettingsResolver(new DispatcherGenerationSettings(New, 4, 250, 5));
        var activity = new ResolveDispatcherVersionActivity(new ResolveDispatcherVersionActivityTests.FakeVersionResolver(null), settings);

        var result = await activity.RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Equal((New, 4, 250, 5), (result.ContractBudgets, result.ContractBudgetsVersion, result.DispatcherRolloverThreshold, result.DispatcherSettingsVersion));
        Assert.Equal(("E2E::Acme::HQ", "three-section-chain"), settings.Received);
    }

    [Fact]
    public async Task Activity_ASettingsPortThatThrows_Throws()
    {
        var activity = new ResolveDispatcherVersionActivity(new ResolveDispatcherVersionActivityTests.FakeVersionResolver(null), new ThrowingSettingsResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(new FakeTaskActivityContext(), Request()));
    }

    /// <summary>Marked obsolete itself so it may call the obsolete one-port constructor, which carries every setting forward.</summary>
    [Fact]
    [Obsolete("Exercises the obsolete one-port constructor.")]
    public async Task Activity_TheObsoleteOnePortConstructor_NamesNoSettings()
    {
        var result = await new ResolveDispatcherVersionActivity(new ResolveDispatcherVersionActivityTests.FakeVersionResolver(null))
            .RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Equal(new ResolveDispatcherVersionResult(), result);
    }

    /// <summary>The composition root's container picks the two-port constructor when both ports are registered.</summary>
    [Fact]
    public async Task Container_WithBothPorts_BuildsTheActivityThatReadsTheSettings()
    {
        var services = new ServiceCollection().AddFrontierWorkflowOrchestration(Configuration());
        services.AddSingleton<IDispatcherVersionResolver>(new ResolveDispatcherVersionActivityTests.FakeVersionResolver(null));
        services.AddSingleton<IDispatcherGenerationSettingsResolver>(new FakeSettingsResolver(new DispatcherGenerationSettings(DispatcherRolloverThreshold: 250, DispatcherSettingsVersion: 5)));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ResolveDispatcherVersionActivity>().RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Equal(250, result.DispatcherRolloverThreshold);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void PortsCheck_PassesOnlyWithBothPorts(bool version, bool settings, bool passes)
    {
        var result = DispatcherPortsCheck.Evaluate(version, settings);

        Assert.Equal(passes, result.Passed);
        if (!version) Assert.Contains(nameof(IDispatcherVersionResolver), result.FailureReason, StringComparison.Ordinal);
        if (!settings) Assert.Contains(nameof(IDispatcherGenerationSettingsResolver), result.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>The check probes the container rather than injecting the ports, so every startup check still resolves when a port is missing.</summary>
    [Fact]
    public async Task PortsCheck_FromTheContainer_ResolvesWithoutThePortsAndFails()
    {
        using var provider = new ServiceCollection().AddFrontierWorkflowOrchestration(Configuration()).BuildServiceProvider();

        var check = Assert.Single(provider.GetServices<IStartupCheck>(), c => c.Name == "DispatcherPorts");

        Assert.False((await check.CheckAsync(CancellationToken.None)).Passed);
    }

    private static ResolveDispatcherVersionRequest Request() => new()
    {
        EngagementId = "E2E::Acme::HQ",
        WorkflowId = "three-section-chain",
        CurrentDefinitionVersion = 1,
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{OrchestrationOptions.ArtifactName}:SandboxMode"] = "false",
    }).Build();

    private sealed class FakeSettingsResolver(DispatcherGenerationSettings settings) : IDispatcherGenerationSettingsResolver
    {
        internal (string, string)? Received { get; private set; }

        public Task<DispatcherGenerationSettings> ResolveAsync(string engagementId, string workflowId, CancellationToken ct)
        {
            Received = (engagementId, workflowId);
            return Task.FromResult(settings);
        }
    }

    private sealed class ThrowingSettingsResolver : IDispatcherGenerationSettingsResolver
    {
        public Task<DispatcherGenerationSettings> ResolveAsync(string engagementId, string workflowId, CancellationToken ct) =>
            throw new InvalidOperationException("settings store unreachable");
    }
}
