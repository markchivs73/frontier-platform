using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="ContextContentComposer"/>.</summary>
public sealed class ContextContentComposerTests
{
    [Fact]
    public async Task ComposeAsync_ValidRequest_ReturnsFilteredTierContent()
    {
        var baseline = new FakeBaselineCatalogueStore("""{"firm-standards":"standards content"}""");
        var dynamic = new FakeEngagementContextStore("""{"engagement_brief":"narrative"}""");
        var composer = new ContextContentComposer(baseline, dynamic, BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = ["engagement_brief"],
        };

        var composed = await composer.ComposeAsync(request, null, null, CancellationToken.None);

        Assert.Equal("""{"firm-standards":"standards content"}""", composed.BaselineContent);
        Assert.Equal("""{"engagement_brief":"narrative"}""", composed.DynamicContent);
        Assert.Equal("{}", composed.RealTimeContent);
    }

    [Fact]
    public async Task ComposeAsync_PinnedEpoch_ReadsThatEpochAndReportsProvenance()
    {
        var dynamic = new FakeEngagementContextStore("""{"engagement_brief":"narrative"}""") { CurrentEpoch = 3 };
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"s"}"""), dynamic, BuildOptions());
        var request = new ContextRequest { EngagementId = "eng-1", AgentRole = "deep-reasoning", BaselineComponents = ["firm-standards"], DynamicFields = ["engagement_brief"] };

        var composed = await composer.ComposeAsync(request, null, pinnedEpoch: 3, CancellationToken.None);

        Assert.Equal(3, dynamic.ReceivedEpoch);
        Assert.Equal(3, composed.DynamicEpoch);
        Assert.Equal("eng-1:ctx:e000003", composed.DynamicRef);
        Assert.Equal(Frontier.Platform.Serialization.CanonicalProfile.Hash("""{"engagement_brief":"narrative"}"""), composed.DynamicContentHash);
    }

    [Fact]
    public async Task ComposeAsync_PinnedEpochDoesNotResolve_ThrowsRatherThanFallingBackToCurrent()
    {
        // S13.60: a run pinned to bytes the store cannot produce is an evidential failure, not a case for "latest".
        var dynamic = new FakeEngagementContextStore("""{"engagement_brief":"narrative"}""") { CurrentEpoch = 3 };
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"s"}"""), dynamic, BuildOptions());
        var request = new ContextRequest { EngagementId = "eng-1", AgentRole = "deep-reasoning", BaselineComponents = ["firm-standards"], DynamicFields = ["engagement_brief"] };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => composer.ComposeAsync(request, null, pinnedEpoch: 2, CancellationToken.None));

        Assert.Contains("epoch 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("eng-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_Unpinned_ReadsCurrentAndReportsItsProvenance()
    {
        var dynamic = new FakeEngagementContextStore("""{"engagement_brief":"narrative"}""") { CurrentEpoch = 5 };
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"s"}"""), dynamic, BuildOptions());
        var request = new ContextRequest { EngagementId = "eng-1", AgentRole = "deep-reasoning", BaselineComponents = ["firm-standards"], DynamicFields = ["engagement_brief"] };

        var composed = await composer.ComposeAsync(request, null, null, CancellationToken.None);

        Assert.Null(dynamic.ReceivedEpoch);
        Assert.Equal(5, composed.DynamicEpoch);
    }

    [Fact]
    public async Task ComposeAsync_NoDynamicContextStored_ReportsNoProvenance()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"s"}"""), new FakeEngagementContextStore(null), BuildOptions());
        var request = new ContextRequest { EngagementId = "eng-1", AgentRole = "deep-reasoning", BaselineComponents = ["firm-standards"], DynamicFields = [] };

        var composed = await composer.ComposeAsync(request, null, null, CancellationToken.None);

        Assert.Null(composed.DynamicEpoch);
        Assert.Null(composed.DynamicRef);
        Assert.Null(composed.DynamicContentHash);
    }

    [Fact]
    public async Task ComposeAsync_NoDynamicContextStored_TreatsMissingContextAsEmptyObject()
    {
        var baseline = new FakeBaselineCatalogueStore("""{"firm-standards":"standards content"}""");
        var dynamic = new FakeEngagementContextStore(null);
        var composer = new ContextContentComposer(baseline, dynamic, BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        };

        var composed = await composer.ComposeAsync(request, null, null, CancellationToken.None);

        Assert.Equal("{}", composed.DynamicContent);
    }

    [Fact]
    public async Task ComposeAsync_BaselineCatalogueNotRegistered_ThrowsInvalidOperationException()
    {
        var baseline = new FakeBaselineCatalogueStore(null);
        var dynamic = new FakeEngagementContextStore("{}");
        var composer = new ContextContentComposer(baseline, dynamic, BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => composer.ComposeAsync(request, null, null, CancellationToken.None));

        Assert.Contains("2026-q2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_NullRequest_ThrowsArgumentNullException()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("{}"), new FakeEngagementContextStore("{}"), BuildOptions());

        await Assert.ThrowsAsync<ArgumentNullException>(() => composer.ComposeAsync(null!, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComposeAsync_InvalidRequest_ThrowsContractViolationException()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("{}"), new FakeEngagementContextStore("{}"), BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = [],
            DynamicFields = [],
        };

        await Assert.ThrowsAsync<ContractViolationException>(() => composer.ComposeAsync(request, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComposeAsync_RevisionNoteRequested_RendersHitlRevisionNoteRealTimeContent()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"standards content"}"""), new FakeEngagementContextStore("{}"), BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
            RequiresRealTime = true,
            RealTimeSources = ["hitl-revision-note"],
        };

        var composed = await composer.ComposeAsync(request, "redo scope", null, CancellationToken.None);

        Assert.Equal("""{"hitl_revision_note":"redo scope"}""", composed.RealTimeContent);
    }

    [Fact]
    public async Task ComposeAsync_RevisionNoteRequestedButNotProvided_RealTimeContentIsEmptyObject()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"standards content"}"""), new FakeEngagementContextStore("{}"), BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
            RequiresRealTime = true,
            RealTimeSources = ["hitl-revision-note"],
        };

        var composed = await composer.ComposeAsync(request, null, null, CancellationToken.None);

        Assert.Equal("{}", composed.RealTimeContent);
    }

    [Fact]
    public async Task ComposeAsync_RevisionNoteProvidedButNotRequested_RealTimeContentIsEmptyObject()
    {
        var composer = new ContextContentComposer(new FakeBaselineCatalogueStore("""{"firm-standards":"standards content"}"""), new FakeEngagementContextStore("{}"), BuildOptions());
        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        };

        var composed = await composer.ComposeAsync(request, "redo scope", null, CancellationToken.None);

        Assert.Equal("{}", composed.RealTimeContent);
    }

    [Fact]
    public void Constructor_NullBaselineStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextContentComposer(null!, new FakeEngagementContextStore("{}"), BuildOptions()));
    }

    [Fact]
    public void Constructor_NullEngagementStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextContentComposer(new FakeBaselineCatalogueStore("{}"), null!, BuildOptions()));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextContentComposer(new FakeBaselineCatalogueStore("{}"), new FakeEngagementContextStore("{}"), null!));
    }

    private static IOptions<ContextAssemblyOptions> BuildOptions() =>
        Options.Create(new ContextAssemblyOptions
        {
            BaselineCatalogueId = "2026-q2",
            BaselineMaxTokens = 1000,
            DynamicMaxTokens = 1000,
            RealTimeMaxTokens = 1000,
            DynamicRefreshInterval = TimeSpan.FromMinutes(5),
        });
}
