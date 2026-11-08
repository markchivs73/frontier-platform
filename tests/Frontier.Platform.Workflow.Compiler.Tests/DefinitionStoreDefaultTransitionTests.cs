using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// GOV-01, GOV-14: a store that has not implemented the retirement or supersession transitions
/// must fail loudly, never report a transition it did not store.
/// </summary>
public sealed class DefinitionStoreDefaultTransitionTests
{
    private readonly IDefinitionStore _store = new InMemoryDefinitionStore();

    [Fact]
    public async Task RetireVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "user:admin", Reason = "r" };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.RetireVersionAsync("wf", 1, retirement, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.RetireVersionAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnretireVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.UnretireVersionAsync("wf", 1, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.UnretireVersionAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupersedeVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.SupersedeVersionAsync("wf", 1, 2, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.SupersedeVersionAsync), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// S13.248: a store that does not override <see cref="IDefinitionStore.ApproveAndPublishAsync"/> keeps
    /// the writes every approval made before it: approve, then the version, then the pointer.
    /// </summary>
    [Fact]
    public async Task ApproveAndPublishAsync_DefaultImplementation_WritesInTurn()
    {
        var store = new Mock<IDefinitionStore> { CallBase = true };
        var calls = new List<string>();
        store.Setup(s => s.ApproveProposalAsync("prop-1", It.IsAny<CancellationToken>())).Callback(() => calls.Add("approve")).ReturnsAsync(true);
        store.Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("version")).ReturnsAsync((DefinitionVersionDocument d, CancellationToken _) => d);
        store.Setup(s => s.SetCurrentVersionAsync("wf", 2, It.IsAny<CancellationToken>())).Callback(() => calls.Add("pointer")).Returns(Task.CompletedTask);

        Assert.True(await store.Object.ApproveAndPublishAsync(Proposal(), Version(), CancellationToken.None));
        Assert.Equal(["approve", "version", "pointer"], calls);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_DefaultImplementation_WritesNothingMoreWhenTheApprovalIsRefused()
    {
        var store = new Mock<IDefinitionStore> { CallBase = true };
        store.Setup(s => s.ApproveProposalAsync("prop-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.False(await store.Object.ApproveAndPublishAsync(Proposal(), Version(), CancellationToken.None));
        store.Verify(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.SetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The default is not atomic, as its remarks say: when the version write fails, the approval has
    /// already landed and the pointer is not moved.
    /// </summary>
    [Fact]
    public async Task ApproveAndPublishAsync_DefaultImplementation_LeavesTheApprovalWhenTheVersionWriteFails()
    {
        var store = new Mock<IDefinitionStore> { CallBase = true };
        store.Setup(s => s.ApproveProposalAsync("prop-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("version taken"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Object.ApproveAndPublishAsync(Proposal(), Version(), CancellationToken.None));

        store.Verify(s => s.ApproveProposalAsync("prop-1", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SetCurrentVersionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_DefaultImplementation_RefusesNullArguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.ApproveAndPublishAsync(null!, Version(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.ApproveAndPublishAsync(Proposal(), null!, CancellationToken.None));
    }

    private static PublishProposalDocument Proposal() => new()
    {
        Id = "prop-1",
        WorkflowId = "wf",
        DraftRevision = "rev-1",
        ProposerId = "user:mark",
        ProposedAtUtc = DateTime.UnixEpoch,
        ValidationReportRef = new ValidationReportRef { DocumentId = "ref" },
        State = ProposalState.InReview,
    };

    private static DefinitionVersionDocument Version() => new()
    {
        Id = "wf:v2",
        WorkflowId = "wf",
        State = "published",
        DefinitionVersion = 2,
        DefinitionHash = "sha256:new",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = DateTime.UnixEpoch,
        ApprovedUtc = DateTime.UnixEpoch,
        ValidationReportRef = "ref",
    };
}
