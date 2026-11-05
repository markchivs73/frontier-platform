using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>Tests for <see cref="ApprovalDecider"/> — doc 06 §9's read-check-replace loop and its lost-race retry.</summary>
public sealed class ApprovalDeciderTests
{
    private const string Engagement = "eng-1";

    [Fact]
    public async Task DecideAsync_PendingRequest_ReplacesUnderTheReadVersionAndReportsDecided()
    {
        var request = HitlFixtures.PendingRequest();
        var documents = new FakeApprovalDocuments(request);

        var result = await Decide(documents, request);

        Assert.Equal(ApprovalDecisionOutcome.Decided, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Decided, result.Request!.Status);
        Assert.Equal(["v1"], documents.ReplaceVersions);
        Assert.Same(result.Request, documents.Stored);
    }

    [Fact]
    public async Task DecideAsync_UnknownRequest_ReportsNotFoundWithoutWriting()
    {
        var documents = new FakeApprovalDocuments(null);

        var result = await Decide(documents, HitlFixtures.PendingRequest());

        Assert.Equal(ApprovalDecisionOutcome.NotFound, result.Outcome);
        Assert.Empty(documents.ReplaceVersions);
    }

    [Fact]
    public async Task DecideAsync_AlreadyDecided_ReportsTheStoredWinnerWithoutWriting()
    {
        var winner = ApprovalDecisionTransitionTests.Decided(HitlFixtures.PendingRequest());
        var documents = new FakeApprovalDocuments(winner);

        var result = await Decide(documents, HitlFixtures.PendingRequest());

        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, result.Outcome);
        Assert.Same(winner, result.Request);
        Assert.Empty(documents.ReplaceVersions);
    }

    [Fact]
    public async Task DecideAsync_LostRaceThenWins_RetriesFromAFreshRead()
    {
        var request = HitlFixtures.PendingRequest();
        var documents = new FakeApprovalDocuments(request) { LosingReplaces = 1 };

        var result = await Decide(documents, request);

        Assert.Equal(ApprovalDecisionOutcome.Decided, result.Outcome);
        Assert.Equal(["v1", "v2"], documents.ReplaceVersions);
    }

    [Fact]
    public async Task DecideAsync_LosesEveryAttempt_StopsAtTheCapAndReportsWhatIsStored()
    {
        var request = HitlFixtures.PendingRequest();
        var documents = new FakeApprovalDocuments(request) { LosingReplaces = int.MaxValue };

        var result = await Decide(documents, request);

        Assert.Equal(ApprovalDecider.DecideAttempts, documents.ReplaceVersions.Count);
        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, result.Outcome);
        Assert.Same(request, result.Request);
    }

    [Fact]
    public async Task DecideAsync_LosesEveryAttemptAndTheRequestIsGone_ReportsNotFound()
    {
        var request = HitlFixtures.PendingRequest();
        var documents = new FakeApprovalDocuments(request) { LosingReplaces = int.MaxValue, VanishAfterReads = ApprovalDecider.DecideAttempts };

        var result = await Decide(documents, request);

        Assert.Equal(ApprovalDecisionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task DecideAsync_InvalidArguments_Throw()
    {
        var decider = new ApprovalDecider(new FakeApprovalDocuments(null));
        var decision = ApprovalDecisionTransitionTests.DecisionFor(HitlFixtures.PendingRequest());

        await Assert.ThrowsAsync<ArgumentException>(() => decider.DecideAsync(" ", "a", decision, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => decider.DecideAsync(Engagement, " ", decision, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => decider.DecideAsync(Engagement, "a", null!, CancellationToken.None));
    }

    private static Task<ApprovalDecisionResult> Decide(FakeApprovalDocuments documents, ApprovalRequest request) =>
        new ApprovalDecider(documents).DecideAsync(Engagement, request.Id, ApprovalDecisionTransitionTests.DecisionFor(request), CancellationToken.None);

    /// <summary>An in-memory approval document whose version bumps on every read, with scripted lost races.</summary>
    private sealed class FakeApprovalDocuments(ApprovalRequest? stored) : IApprovalDocuments
    {
        private int reads;

        public ApprovalRequest? Stored { get; private set; } = stored;

        public int LosingReplaces { get; init; }

        public int VanishAfterReads { get; init; } = int.MaxValue;

        public List<string> ReplaceVersions { get; } = [];

        public Task<VersionedApproval?> ReadAsync(string engagementId, string approvalId, CancellationToken cancellationToken)
        {
            reads++;
            var visible = reads <= VanishAfterReads ? Stored : null;
            return Task.FromResult(visible is null ? null : new VersionedApproval(visible, $"v{reads}"));
        }

        public Task<bool> TryReplaceAsync(ApprovalRequest request, string expectedVersion, CancellationToken cancellationToken)
        {
            ReplaceVersions.Add(expectedVersion);
            if (ReplaceVersions.Count <= LosingReplaces)
            {
                return Task.FromResult(false);
            }

            Stored = request;
            return Task.FromResult(true);
        }
    }
}
