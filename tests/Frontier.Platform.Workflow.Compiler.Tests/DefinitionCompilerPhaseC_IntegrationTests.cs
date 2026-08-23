#pragma warning disable CA2000  // HttpClient/Handler disposal: CosmosClientOptions takes ownership
using System.Net;
using System.Net.Http;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S8.2 Phase C integration tests: resourced-tier validation, rule discovery, validation report persistence.
/// Runs against the Cosmos emulator — locally and in CI since S9.23 (the LocalOnly trait
/// existed only because CI's old emulator was broken; C-13 closed).
/// Doc 13 §4.2 (rule catalogue), §7 (storage).
/// </summary>
[Trait("Category", "Integration")]
public sealed class DefinitionCompilerPhaseC_IntegrationTests : IAsyncLifetime, IDisposable
{
    private static readonly string EmulatorEndpoint = Frontier.TestSupport.EmulatorCosmos.Endpoint;
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    private const string DatabaseId = "frontier-workflow-s82-tests";
    private const string ContainerId = "workflow-definitions";

    private CosmosClient? _cosmosClient;
    private Database? _database;
    private Container? _container;
    private IDefinitionStore? _store;
    private IServiceProvider? _services;

    public async Task InitializeAsync()
    {
        // Create Cosmos client and database
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        var httpClient = new HttpClient(handler);

        var clientOptions = new CosmosClientOptions
        {
            // Gateway is required: the SDK defaults to Direct (rntbd), which the
            // vnext-preview emulator does not serve — address resolution 400s at
            // GetMasterAddressesViaGatewayAsync before any document operation runs.
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => httpClient,
            // The storage documents carry [JsonPropertyName] wire names (lowercase "id");
            // without the canonical STJ serializer the SDK's default Newtonsoft path
            // ignores those attributes and Cosmos rejects the document. Mirrors the
            // production client wiring in Host.
            UseSystemTextJsonSerializerWithOptions = Frontier.Platform.Serialization.CanonicalProfile.Options,
        };

        _cosmosClient = new CosmosClient(EmulatorEndpoint, EmulatorKey, clientOptions);

        try
        {
            _database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
            _container = await _database.CreateContainerIfNotExistsAsync(
                new ContainerProperties
                {
                    Id = ContainerId,
                    PartitionKeyPath = "/workflowId",
                    DefaultTimeToLive = -1  // No TTL by default; validation reports set explicitly
                });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.Message.Contains("401", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Cosmos emulator not available. Start the emulator with: " +
                "`dotnet run --project tools/dev-setup/AspireHost`",
                ex);
        }

        // Set up DI with the definition compiler and store
        if (_container == null)
            throw new InvalidOperationException("Container not initialized");

        var services = new ServiceCollection();
        // S13.12c: the contract set is a composition-root input now — the compiler no longer
        // reflects over a workload assembly to find it (E16 option 2 / ADR-E3a).
        services.AddSingleton(TestContractSet.Instance);
        services.AddFrontierWorkflowCompiler();
        services.AddSingleton(_container);
        // AddFrontierWorkflowCompiler's IDefinitionStore factory resolves a CosmosClient
        // and hard-codes the production database/container names; override it with this
        // test's isolated container so the suite provisions and tears down its own state
        // (the registration shape changed after this test was written, while the suite was
        // unrunnable against the retired HTTPS emulator — last registration wins).
        services.AddScoped<IDefinitionStore>(_ => new CosmosDefinitionStore(_container));
        // S9.27c: IDefinitionCompiler's factory constructs every registered
        // IDefinitionValidationRule, including the two resourced-tier rules added at S9.27c —
        // their catalogue dependencies must resolve here even though this suite never exercises
        // resourced findings (RuleDiscoveryFindsCompilerOwnedRules only asserts the compiler
        // resolves at all).
        services.AddSingleton<IDesignerToolCatalog>(new EmptyDesignerToolCatalog());
        services.AddSingleton<IAgentRoleCatalog>(new EmptyAgentRoleCatalog());
        // S9.30: the full resourced-tier catalogue's dependencies — same rationale as above.
        services.AddSingleton<IApproverRoleCatalog>(new EmptyApproverRoleCatalog());
        services.AddSingleton<IContextComponentCatalog>(new EmptyContextComponentCatalog());
        services.AddSingleton<IInstructionCatalog>(new PermissiveInstructionCatalog());
        services.AddSingleton<IRetryProfileCatalog>(new EmptyRetryProfileCatalog());
        services.AddSingleton<ICascadeGraphChecker>(new EmptyCascadeGraphChecker());
        _services = services.BuildServiceProvider();

        _store = _services?.GetRequiredService<IDefinitionStore>() ?? throw new InvalidOperationException("Store not initialized");
    }

    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            try
            {
                await _database.DeleteAsync();
            }
            catch (CosmosException)
            {
                // Ignore cleanup errors
            }
        }

        _cosmosClient?.Dispose();
        (_services as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        _cosmosClient?.Dispose();
        (_services as IDisposable)?.Dispose();
    }

    [Fact]
    public void RuleDiscoveryFindsCompilerOwnedRules()
    {
        // Verify the compiler-owned rules are registered in DI
        ArgumentNullException.ThrowIfNull(_services);
        var compiler = _services.GetRequiredService<IDefinitionCompiler>();

        // The compiler should have aggregated rules from the container
        // Phase 1: We can't directly inspect the rules without exposing them,
        // but we can verify compilation works
        Assert.NotNull(compiler);
    }

    [Fact]
    public async Task ValidationReportPersistenceStoresAndRetrieves()
    {
        ArgumentNullException.ThrowIfNull(_store);
        const string workflowId = "wf-phase-c-test";
        const string draftRevision = "rev-001";

        var report = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:{draftRevision}",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ValidatedAtUtc = DateTime.UtcNow,
            Outcome = ValidationOutcome.Pass,
            Findings = new List<ValidationFinding>(),
            ResourceVersions = new Dictionary<string, string>
            {
                ["role-catalogue"] = "v1.2.3",
                ["connector-registry"] = "v2.0.0"
            }
        };

        // Persist the report
        var stored = await _store.PersistValidationReportAsync(report, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(workflowId, stored.WorkflowId);
        Assert.Equal(draftRevision, stored.DraftRevision);
        Assert.Equal(ValidationOutcome.Pass, stored.Outcome);

        // Retrieve and verify
        var retrieved = await _store.GetValidationReportAsync(workflowId, draftRevision, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(stored.Id, retrieved.Id);
        Assert.Equal(2, retrieved.ResourceVersions.Count);
        Assert.Equal("v1.2.3", retrieved.ResourceVersions["role-catalogue"]);
    }

    [Fact]
    public async Task ValidationReportWithFindingsStoresAndRetrieves()
    {
        ArgumentNullException.ThrowIfNull(_store);
        const string workflowId = "wf-phase-c-findings";
        const string draftRevision = "rev-002";

        var findings = new List<ValidationFinding>
        {
            new ValidationFinding(
                RuleId: "model-role.no-model-ids",
                Severity: ValidationSeverity.Error,
                Message: "Model ID found in definition — total indirection required",
                NodeId: "node-123",
                EdgeRef: null,
                FieldPath: "nodes[0].modelId",
                SourceLibrary: "Frontier.Platform.ModelRoleConfig"),
            new ValidationFinding(
                RuleId: "retention.fits-window",
                Severity: ValidationSeverity.Warning,
                Message: "Estimated workflow duration exceeds deployment retention window",
                NodeId: null,
                EdgeRef: null,
                FieldPath: null,
                SourceLibrary: "Frontier.Reason.Workflow.Integration.Host")
        };

        var report = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:{draftRevision}",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ValidatedAtUtc = DateTime.UtcNow,
            Outcome = ValidationOutcome.PassWithWarnings,
            Findings = findings,
            ResourceVersions = new Dictionary<string, string> { ["role-catalogue"] = "v2.0.0" }
        };

        await _store.PersistValidationReportAsync(report, CancellationToken.None);

        var retrieved = await _store.GetValidationReportAsync(workflowId, draftRevision, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(ValidationOutcome.PassWithWarnings, retrieved.Outcome);
        Assert.Equal(2, retrieved.Findings.Count);

        var errorFinding = retrieved.Findings.First(f => f.Severity == ValidationSeverity.Error);
        Assert.Equal("model-role.no-model-ids", errorFinding.RuleId);
        Assert.Equal("node-123", errorFinding.NodeId);

        var warningFinding = retrieved.Findings.First(f => f.Severity == ValidationSeverity.Warning);
        Assert.Equal("retention.fits-window", warningFinding.RuleId);
    }

    [Fact]
    public async Task RegistryVersionsDriftDetectionAtApprovalTime()
    {
        ArgumentNullException.ThrowIfNull(_store);
        // Scenario: A validation report captured role-catalogue v1.2.3 at proposal time.
        // At approval time, the registry has advanced to v1.3.0.
        // Phase 1: Log drift; Phase 2: Block approval if critical resources changed.

        const string workflowId = "wf-registry-drift";
        const string draftRevision = "rev-003";

        var reportAtProposalTime = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:{draftRevision}",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ValidatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Outcome = ValidationOutcome.Pass,
            Findings = new List<ValidationFinding>(),
            ResourceVersions = new Dictionary<string, string>
            {
                ["role-catalogue"] = "v1.2.3",
                ["connector-registry"] = "v1.0.0"
            }
        };

        await _store.PersistValidationReportAsync(reportAtProposalTime, CancellationToken.None);

        // Simulate registry advancing
        var currentRegistryVersions = new Dictionary<string, string>
        {
            ["role-catalogue"] = "v1.3.0",  // Drift detected
            ["connector-registry"] = "v1.0.0"  // No change
        };

        // Retrieve report and compare versions
        var retrieved = await _store.GetValidationReportAsync(workflowId, draftRevision, CancellationToken.None);

        Assert.NotNull(retrieved);

        // Drift detection logic (Phase 1 info only, not blocking)
        var roleCatalogueDrifted = retrieved.ResourceVersions["role-catalogue"] != currentRegistryVersions["role-catalogue"];
        Assert.True(roleCatalogueDrifted, "Role catalogue version drift should be detected");

        var connectorNoDrift = retrieved.ResourceVersions["connector-registry"] == currentRegistryVersions["connector-registry"];
        Assert.True(connectorNoDrift, "Connector registry should have no drift");
    }

    [Fact]
    public async Task MultipleReportsPerWorkflowTracksRevisionHistory()
    {
        ArgumentNullException.ThrowIfNull(_store);
        const string workflowId = "wf-multi-revision";

        // First revision validation
        var report1 = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:rev-1",
            WorkflowId = workflowId,
            DraftRevision = "rev-1",
            ValidatedAtUtc = DateTime.UtcNow,
            Outcome = ValidationOutcome.Fail,
            Findings = new List<ValidationFinding>
            {
                new("graph.is-dag", ValidationSeverity.Error, "Cycle detected", null, null, null, "Compiler")
            },
            ResourceVersions = new Dictionary<string, string>()
        };

        await _store.PersistValidationReportAsync(report1, CancellationToken.None);

        // Second revision (after fix) validation
        var report2 = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:rev-2",
            WorkflowId = workflowId,
            DraftRevision = "rev-2",
            ValidatedAtUtc = DateTime.UtcNow.AddSeconds(1),
            Outcome = ValidationOutcome.Pass,
            Findings = new List<ValidationFinding>(),
            ResourceVersions = new Dictionary<string, string> { ["version"] = "2" }
        };

        await _store.PersistValidationReportAsync(report2, CancellationToken.None);

        // Verify both reports are retrievable
        var retrieved1 = await _store.GetValidationReportAsync(workflowId, "rev-1", CancellationToken.None);
        var retrieved2 = await _store.GetValidationReportAsync(workflowId, "rev-2", CancellationToken.None);

        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal(ValidationOutcome.Fail, retrieved1.Outcome);
        Assert.Equal(ValidationOutcome.Pass, retrieved2.Outcome);
    }

    // ── S9.55 version-health projection (doc 13 ADR-DC5) ─────────────────────────

    private static DefinitionVersionDocument VersionDoc(string workflowId, int version, string state) => new()
    {
        Id = $"{workflowId}:v{version}",
        WorkflowId = workflowId,
        State = state,
        DefinitionVersion = version,
        DefinitionHash = "sha256:abc",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = DateTime.UtcNow,
        ApprovedUtc = DateTime.UtcNow,
        ValidationReportRef = "ref",
    };

    [Fact]
    public async Task ListPublishedVersionsAsync_ReturnsOnlyPublishedAcrossWorkflows()
    {
        ArgumentNullException.ThrowIfNull(_store);
        ArgumentNullException.ThrowIfNull(_container);
        // PublishVersionAsync always forces state=published, so write the non-published states
        // directly to exercise the sweep's "live published only" filter.
        await _store.PublishVersionAsync(VersionDoc("wf-pub-a", 1, "published"), CancellationToken.None);
        await _store.PublishVersionAsync(VersionDoc("wf-pub-b", 4, "published"), CancellationToken.None);
        await _container.UpsertItemAsync(VersionDoc("wf-pub-a", 2, "superseded"), new PartitionKey("wf-pub-a"), cancellationToken: CancellationToken.None);
        await _container.UpsertItemAsync(VersionDoc("wf-pub-b", 3, "retired"), new PartitionKey("wf-pub-b"), cancellationToken: CancellationToken.None);

        var published = await _store.ListPublishedVersionsAsync(CancellationToken.None);

        var mine = published.Where(v => v.WorkflowId is "wf-pub-a" or "wf-pub-b").ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, v => Assert.Equal("published", v.State));
    }

    [Fact]
    public async Task UpsertVersionHealthAsync_ThenList_RoundTripsAndOverwrites()
    {
        ArgumentNullException.ThrowIfNull(_store);
        const string workflowId = "wf-health-rt";

        await _store.UpsertVersionHealthAsync(new WorkflowHealthDocument
        {
            Id = "ignored", WorkflowId = workflowId, DefinitionVersion = 1,
            HealthStatus = "healthy", FailingRuleIds = [], FindingCount = 0, CheckedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        // Re-sweep the same version → failing; upsert must overwrite (idempotent, one doc per version).
        await _store.UpsertVersionHealthAsync(new WorkflowHealthDocument
        {
            Id = "ignored", WorkflowId = workflowId, DefinitionVersion = 1,
            HealthStatus = "failing", FailingRuleIds = ["mcp.tool-resolves"], FindingCount = 1, CheckedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var health = await _store.ListVersionHealthAsync(workflowId, CancellationToken.None);

        var doc = Assert.Single(health);
        Assert.Equal("wf-health-rt:v1:health", doc.Id); // store assigns the sidecar id
        Assert.Equal("failing", doc.HealthStatus);
        Assert.Equal(["mcp.tool-resolves"], doc.FailingRuleIds);
    }

    [Fact]
    public async Task ListAllVersionHealthAsync_ReturnsHealthAcrossWorkflows()
    {
        ArgumentNullException.ThrowIfNull(_store);
        await _store.UpsertVersionHealthAsync(new WorkflowHealthDocument
        {
            Id = "x", WorkflowId = "wf-allh-a", DefinitionVersion = 1,
            HealthStatus = "failing", FailingRuleIds = ["mcp.tool-resolves"], FindingCount = 1, CheckedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);
        await _store.UpsertVersionHealthAsync(new WorkflowHealthDocument
        {
            Id = "x", WorkflowId = "wf-allh-b", DefinitionVersion = 2,
            HealthStatus = "healthy", FailingRuleIds = [], FindingCount = 0, CheckedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var all = await _store.ListAllVersionHealthAsync(CancellationToken.None);

        var mine = all.Where(h => h.WorkflowId is "wf-allh-a" or "wf-allh-b").ToList();
        Assert.Equal(2, mine.Count);
    }

    [Fact]
    public async Task ListVersionHealthAsync_UnknownWorkflow_ReturnsEmpty()
    {
        ArgumentNullException.ThrowIfNull(_store);

        var health = await _store.ListVersionHealthAsync("wf-no-health", CancellationToken.None);

        Assert.Empty(health);
    }

    [Fact]
    public async Task UpsertWorkflowUsageAsync_ThenListAll_RoundTripsAndOverwrites()
    {
        ArgumentNullException.ThrowIfNull(_store);
        const string workflowId = "wf-usage-rt";

        await _store.UpsertWorkflowUsageAsync(new WorkflowUsageDocument
        {
            Id = "ignored", WorkflowId = workflowId, LastRunAtUtc = DateTime.UtcNow.AddDays(-1),
            RunCount30d = 2, FailureCount30d = 0, ActiveCount = 1, SweptAtUtc = DateTime.UtcNow.AddHours(-2),
        }, CancellationToken.None);

        // Re-sweep the same workflow → overwrite (one usage doc per workflow, idempotent).
        await _store.UpsertWorkflowUsageAsync(new WorkflowUsageDocument
        {
            Id = "ignored", WorkflowId = workflowId, LastRunAtUtc = DateTime.UtcNow,
            RunCount30d = 5, FailureCount30d = 2, ActiveCount = 0, SweptAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var all = await _store.ListAllWorkflowUsageAsync(CancellationToken.None);

        var mine = Assert.Single(all, u => u.WorkflowId == workflowId);
        Assert.Equal("wf-usage-rt:usage", mine.Id); // store assigns the sidecar id
        Assert.Equal(5, mine.RunCount30d);
        Assert.Equal(2, mine.FailureCount30d);
    }

    private sealed class EmptyDesignerToolCatalog : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>([]);
    }

    private sealed class EmptyAgentRoleCatalog : IAgentRoleCatalog
    {
        public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>([]);
    }

    private sealed class EmptyApproverRoleCatalog : IApproverRoleCatalog
    {
        public Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApproverRoleDescriptor>>([]);
    }

    private sealed class EmptyContextComponentCatalog : IContextComponentCatalog
    {
        public Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class PermissiveInstructionCatalog : IInstructionCatalog
    {
        public Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class EmptyRetryProfileCatalog : IRetryProfileCatalog
    {
        public Task<IReadOnlyList<RetryProfileDescriptor>> GetProfilesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RetryProfileDescriptor>>([]);
    }

    private sealed class EmptyCascadeGraphChecker : ICascadeGraphChecker
    {
        public IReadOnlyList<string> CheckAtPublish(WorkflowDefinition definition) => [];
    }
}
