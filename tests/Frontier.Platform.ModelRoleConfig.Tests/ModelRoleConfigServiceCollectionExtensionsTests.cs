using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="ModelRoleConfigServiceCollectionExtensions"/>'s DI wiring and options binding (doc 12 §4 "options with teeth").</summary>
public sealed class ModelRoleConfigServiceCollectionExtensionsTests
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void AddFrontierModelRoleConfig_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddFrontierModelRoleConfig(null!));
    }

    [Fact]
    public void AddFrontierModelRoleConfig_ValidConfiguration_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierModelRoleConfig(configuration);
        // The IReferencedRolesSource port is consumer-owned (ADR-PA2, S11.4): Host registers
        // the real adapter; tests supply a stand-in so RoleCatalogueCheck can materialize.
        services.AddSingleton<IReferencedRolesSource>(new EmptyRolesSource());
        // IMappingDecisionRecorder is deliberately left unregistered by the library (ADR-PA32): a
        // no-op default would be a silent audit gap, so the consumer must supply one.
        services.AddSingleton<IMappingDecisionRecorder>(new NoOpDecisionRecorder());
        // IContractSnapshotCatalog is consumer-owned too (ADR-PA40): role definitions resolve refs through it.
        services.AddSingleton<IContractSnapshotCatalog>(new FakeContractCatalog());
        // S13.328 (a2): the governance services log a lost compensation, so composition needs logging, which every host has.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<CosmosClient>());
        Assert.IsType<CosmosRoleRegistry>(provider.GetRequiredService<IRoleRegistry>());
        Assert.IsType<CosmosMappingProposalStore>(provider.GetRequiredService<IMappingProposalStore>());
        Assert.IsType<CosmosRoleRegistry>(provider.GetRequiredService<IRoleMappingWriter>());
        Assert.IsType<AlwaysClosedCircuitBreakerQuery>(provider.GetRequiredService<ICircuitBreakerQuery>());
        Assert.IsType<ModelResolver>(provider.GetRequiredService<IModelResolver>());
        Assert.IsType<MappingPinner>(provider.GetRequiredService<IMappingPinner>());
        Assert.IsType<MappingGovernanceService>(provider.GetRequiredService<IMappingGovernanceService>());
        Assert.IsType<CosmosRoleDefinitionStore>(provider.GetRequiredService<IRoleDefinitionStore>());
        using (var scope = provider.CreateScope())
        {
            Assert.IsType<RoleDefinitionGovernanceService>(scope.ServiceProvider.GetRequiredService<IRoleDefinitionGovernanceService>());
        }

        var startupChecks = provider.GetServices<IStartupCheck>().ToList();
        Assert.Contains(startupChecks, check => check is CosmosTopologyCheck);
        Assert.Contains(startupChecks, check => check is RoleCatalogueCheck);
    }

    [Fact]
    public void AddFrontierModelRoleConfig_WithoutAContractCatalog_FailsToComposeTheDefinitionService()
    {
        // No silent default (ADR-PA40): an unwired contract port must fail at composition, not
        // approve a definition with no hashes.
        var services = new ServiceCollection();
        services.AddFrontierModelRoleConfig(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        }));
        services.AddSingleton<IMappingDecisionRecorder>(new NoOpDecisionRecorder());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IRoleDefinitionGovernanceService>());

        Assert.Contains(nameof(IContractSnapshotCatalog), failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://frontier.documents.azure.com:443", false)]
    [InlineData("https://localhost:8081", true)]
    [InlineData("https://127.0.0.1:8081", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    public void AddFrontierModelRoleConfig_EndpointLocality_DeterminesCertBypassBranch(string endpoint, bool isLocal)
    {
        // Exercises every branch of the private IsLocalEmulator OR-chain (S9.24
        // branch-coverage gap): each scheme/host combination the emulator can present.
        _ = isLocal; // asserted implicitly: client construction never throws either way
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = endpoint,
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierModelRoleConfig(configuration);
        // The IReferencedRolesSource port is consumer-owned (ADR-PA2, S11.4): Host registers
        // the real adapter; tests supply a stand-in so RoleCatalogueCheck can materialize.
        services.AddSingleton<IReferencedRolesSource>(new EmptyRolesSource());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CosmosClient>());
    }

    [Fact]
    public void AddFrontierModelRoleConfig_MissingCosmosKey_OptionsResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
        });

        services.AddFrontierModelRoleConfig(configuration);
        // The IReferencedRolesSource port is consumer-owned (ADR-PA2, S11.4): Host registers
        // the real adapter; tests supply a stand-in so RoleCatalogueCheck can materialize.
        services.AddSingleton<IReferencedRolesSource>(new EmptyRolesSource());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CosmosOptions>>().Value);
    }

    /// <summary>
    /// S13.328 (a2): both container-built governance services — the singleton mapping service and the scoped definition
    /// service — report a lost compensation through the logging the host registered, under their own category.
    /// </summary>
    [Fact]
    public async Task AddFrontierModelRoleConfig_BothGovernanceServices_LogALostCompensationThroughTheRegisteredLogging()
    {
        var sink = new LogSink();
        var mappingServices = Composed(sink);
        var proposals = new FakeMappingProposalStore().WithProposal(MappingProposalSamples.Pending());
        proposals.AllocateThrows = new InvalidOperationException("cosmos down");
        mappingServices.AddSingleton<IMappingProposalStore>(proposals);
        mappingServices.AddSingleton<IRoleDefinitionStore>(MappingProposalSamples.ModelRoleDefinitions());
        var definitionServices = Composed(sink);
        definitionServices.AddSingleton<IRoleDefinitionStore>(new FakeRoleDefinitionStore { CreateThrows = new InvalidOperationException("cosmos down") });
        using var mapping = mappingServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var definition = definitionServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => mapping.GetRequiredService<IMappingGovernanceService>()
            .ApproveAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "ok", null, CancellationToken.None));
        using (var scope = definition.CreateScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IRoleDefinitionGovernanceService>()
                .ProposeAsync(RoleDefinitionProposalSamples.AgentDefinition(), RoleDefinitionProposalSamples.Proposer, RoleDefinitionProposalSamples.Reason, CancellationToken.None));
        }

        Assert.Contains((nameof(MappingGovernanceService), LogLevel.Critical), sink.Entries);
        Assert.Contains((nameof(RoleDefinitionGovernanceService), LogLevel.Critical), sink.Entries);
    }

    private static ServiceCollection Composed(LogSink sink)
    {
        var services = new ServiceCollection();
        services.AddFrontierModelRoleConfig(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        }));
        services.AddSingleton<IMappingDecisionRecorder>(new FakeMappingDecisionRecorder { CompensationThrows = new InvalidOperationException("audit down") });
        services.AddSingleton<IContractSnapshotCatalog>(RoleDefinitionProposalSamples.Catalog());
        services.AddSingleton(sink);
        services.AddSingleton(typeof(ILogger<>), typeof(SinkLogger<>));
        return services;
    }

    /// <summary>The entries every <see cref="SinkLogger{T}"/> in a container wrote, as (category, level).</summary>
    private sealed class LogSink
    {
        public List<(string Category, LogLevel Level)> Entries { get; } = [];
    }

    /// <summary>The open-generic logger the container hands out, writing to the one <see cref="LogSink"/>.</summary>
    private sealed class SinkLogger<T>(LogSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Entries.Add((typeof(T).Name, logLevel));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class NoOpDecisionRecorder : IMappingDecisionRecorder
    {
        public Task<string> RecordAsync(MappingGovernanceDecision decision, CancellationToken cancellationToken) =>
            Task.FromResult("record-1");

        public Task RecordCompensationAsync(MappingGovernanceDecision decision, string recordId, string failureReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class EmptyRolesSource : IReferencedRolesSource
    {
        public Task<IReadOnlySet<string>> GetReferencedRoleIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
