using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.62 — <see cref="RefreshDynamicContextActivity"/>, the activity half of ADR-CR1's loop. The
/// walk tests drive the orchestrator's decision through a stubbed activity; these cover the
/// activity's own wiring: it renders through the consumer's port and merges through the refresher,
/// and it is registered while its port deliberately is not.
/// </summary>
public sealed class RefreshDynamicContextActivityTests
{
    private const string Reason = "CRM_pricing_changed";

    private static RefreshDynamicContextRequest Request() => new()
    {
        EngagementId = "eng-1",
        Reason = Reason,
        ChangedFields = ["commercial_terms"],
        Components = ["client_profile"],
    };

    /// <summary>
    /// The activity passes the request's engagement, components and reason to the producer, then
    /// hands what the producer rendered to the refresher and returns its result unchanged.
    /// </summary>
    [Fact]
    public async Task RunAsync_RendersThroughThePortAndMergesThroughTheRefresher()
    {
        var producer = new FakeContentProducer();
        var refresher = new FakeRefresher(new DynamicContextRefreshResult(Refreshed: true, Epoch: 7, ContentHash: "refreshed-hash"));
        var activity = new RefreshDynamicContextActivity(producer, refresher);

        var result = await activity.RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Equal("eng-1", producer.ReceivedEngagementId);
        Assert.Equal(["client_profile"], producer.ReceivedComponents!);
        Assert.Equal(Reason, producer.ReceivedReason);
        Assert.Equal(producer.Rendered, refresher.ReceivedComponents);
        Assert.Equal(Reason, refresher.ReceivedReason);
        Assert.Equal(7, result.Epoch);
        Assert.Equal("refreshed-hash", result.ContentHash);
    }

    /// <summary>A signal naming no components reaches the producer as <see langword="null"/> — "producer decides scope", not "refresh nothing".</summary>
    [Fact]
    public async Task RunAsync_SignalNamedNoComponents_PassesNullThrough()
    {
        var producer = new FakeContentProducer();
        var activity = new RefreshDynamicContextActivity(producer, new FakeRefresher(new DynamicContextRefreshResult(false, 0, "h")));

        await activity.RunAsync(new FakeTaskActivityContext(), Request() with { Components = null });

        Assert.Null(producer.ReceivedComponents);
    }

    /// <summary>Guard branches at the activity's boundary.</summary>
    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var activity = new RefreshDynamicContextActivity(new FakeContentProducer(), new FakeRefresher(new DynamicContextRefreshResult(false, 0, "h")));

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(new FakeTaskActivityContext(), null!));
    }

    /// <summary>Constructor guards: neither collaborator is optional.</summary>
    [Fact]
    public void Constructor_NullCollaborators_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RefreshDynamicContextActivity(null!, new FakeRefresher(new DynamicContextRefreshResult(false, 0, "h"))));
        Assert.Throws<ArgumentNullException>(() => new RefreshDynamicContextActivity(new FakeContentProducer(), null!));
    }

    /// <summary>A request that names no changed fields carries an empty list, never null — the field is omit-safe for the activity's own callers.</summary>
    [Fact]
    public void RefreshDynamicContextRequest_ChangedFields_DefaultsToEmpty() =>
        Assert.Empty(new RefreshDynamicContextRequest { EngagementId = "eng-1", Reason = Reason }.ChangedFields);

    /// <summary>
    /// The engine registers the activity but never the port behind it: what a dynamic component
    /// <em>is</em> is the consumer's knowledge (doc 18 §1, ADR-EC1), and a default here would let a
    /// misconfigured deployment refresh against the wrong renderer instead of failing to start.
    /// </summary>
    [Fact]
    public void AddFrontierWorkflowOrchestration_RegistersTheActivityButNotItsPort()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OrchestrationOptions.ArtifactName}:SandboxMode"] = "false",
        }).Build();

        var registered = new ServiceCollection()
            .AddFrontierWorkflowOrchestration(configuration)
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();

        Assert.Contains(typeof(RefreshDynamicContextActivity), registered);
        Assert.DoesNotContain(typeof(IDynamicContextContentProducer), registered);
    }

    /// <summary>Records what the engine asked for and returns one rendered component.</summary>
    private sealed class FakeContentProducer : IDynamicContextContentProducer
    {
        public string? ReceivedEngagementId { get; private set; }
        public IReadOnlyList<string>? ReceivedComponents { get; private set; }
        public string? ReceivedReason { get; private set; }

        public IReadOnlyDictionary<string, string> Rendered { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_profile"] = """{"client_id":"client::acme","data_quality":"enriched"}""",
        };

        public Task<IReadOnlyDictionary<string, string>> ProduceAsync(string engagementId, IReadOnlyList<string>? components, string refreshReason, CancellationToken ct)
        {
            ReceivedEngagementId = engagementId;
            ReceivedComponents = components;
            ReceivedReason = refreshReason;
            return Task.FromResult(Rendered);
        }
    }

    /// <summary>Captures the merge call and returns a fixed outcome.</summary>
    private sealed class FakeRefresher(DynamicContextRefreshResult result) : IDynamicContextRefresher
    {
        public IReadOnlyDictionary<string, string>? ReceivedComponents { get; private set; }
        public string? ReceivedReason { get; private set; }

        public Task<DynamicContextRefreshResult> RefreshDynamicAsync(EngagementId engagementId, string newDynamicContent, string refreshReason, CancellationToken ct) =>
            throw new NotSupportedException("The activity refreshes components, never a whole document.");

        public Task<DynamicContextRefreshResult> RefreshComponentsAsync(EngagementId engagementId, IReadOnlyDictionary<string, string> components, string refreshReason, CancellationToken ct)
        {
            ReceivedComponents = components;
            ReceivedReason = refreshReason;
            return Task.FromResult(result);
        }
    }
}
