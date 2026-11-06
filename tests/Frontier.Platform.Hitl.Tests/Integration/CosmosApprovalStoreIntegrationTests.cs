using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl.Tests.Integration;

/// <summary>
/// S6.11a integration tests for <see cref="CosmosApprovalStore.GetDecidedAsync"/> against
/// the local Cosmos emulator (doc 12 §8, cosmos-conventions: "integration tests against
/// the emulator, not SDK mocks"). Creates and tears down its own database/container so it
/// doesn't depend on <c>tools/dev-setup/cosmos-init.py</c> having been run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosApprovalStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-hitl-approval-tests";

    private CosmosClient client = null!;
    private CosmosApprovalStore store = null!;

    /// <summary>Creates a canonical-profile-wired <see cref="CosmosClient"/> against the emulator and provisions the <c>approvals</c> container.</summary>
    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, EmulatorKey, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                CheckCertificateRevocationList = true,
            }),
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        await database.Database.CreateContainerIfNotExistsAsync(CosmosApprovalStore.ContainerName, "/engagement_id");

        var cosmosOptions = Options.Create(new CosmosOptions { Database = DatabaseId });
        var decidedQuery = new DecidedApprovalQuery(client, cosmosOptions);
        store = new CosmosApprovalStore(client, cosmosOptions, decidedQuery);
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/> (doc 06 §9 store is the system under test, not the disposal target).</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task GetDecidedAsync_ReturnsOnlyDecidedRequests()
    {
        var pending = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-pending" };
        var decided = HitlFixtures.PendingRequest() with
        {
            Id = $"req-{Guid.NewGuid():N}",
            EngagementId = "eng-decided",
            Status = ApprovalRequestStatus.Decided,
            Decision = new HitlDecision
            {
                GateId = "gate-business-1",
                RequestId = "req-decided",
                ApproverId = "approver-1",
                Kind = DecisionKind.Approve,
                DecidedAtUtc = new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc),
            },
        };
        var escalated = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-escalated", Status = ApprovalRequestStatus.Escalated };
        var expired = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-expired", Status = ApprovalRequestStatus.Expired };

        await store.UpsertAsync(pending, CancellationToken.None);
        await store.UpsertAsync(decided, CancellationToken.None);
        await store.UpsertAsync(escalated, CancellationToken.None);
        await store.UpsertAsync(expired, CancellationToken.None);

        var results = await store.GetDecidedAsync(CancellationToken.None);

        Assert.Contains(results, r => r.Id == decided.Id);
        Assert.DoesNotContain(results, r => r.Id == pending.Id);
        Assert.DoesNotContain(results, r => r.Id == escalated.Id);
        Assert.DoesNotContain(results, r => r.Id == expired.Id);
    }

    [Fact]
    public async Task DecideAsync_PendingRequest_EmbedsTheDecisionAndBecomesVisibleToTheSweep()
    {
        var pending = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-decide" };
        await store.UpsertAsync(pending, CancellationToken.None);
        var decision = ApprovalDecisionTransitionTests.DecisionFor(pending);

        var result = await store.DecideAsync(pending.EngagementId, pending.Id, decision, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Decided, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Decided, result.Request!.Status);
        Assert.Equal(decision, result.Request.Decision);
        var stored = Assert.Single(await store.GetDecidedAsync(CancellationToken.None), r => r.Id == pending.Id);
        Assert.Equal(decision, stored.Decision);
    }

    [Fact]
    public async Task DecideAsync_SecondDecision_LosesAndIsToldWhoWon()
    {
        var pending = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-race" };
        await store.UpsertAsync(pending, CancellationToken.None);
        var first = ApprovalDecisionTransitionTests.DecisionFor(pending);
        var second = first with { ApproverId = "user:approver-2", Kind = DecisionKind.Reject };
        await store.DecideAsync(pending.EngagementId, pending.Id, first, CancellationToken.None);

        var result = await store.DecideAsync(pending.EngagementId, pending.Id, second, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, result.Outcome);
        Assert.Equal(first, result.Request!.Decision);
    }

    [Fact]
    public async Task DecideAsync_EscalatedRequest_StillTakesTheDecision()
    {
        var escalated = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-escalated-decide", Status = ApprovalRequestStatus.Escalated };
        await store.UpsertAsync(escalated, CancellationToken.None);

        var result = await store.DecideAsync(escalated.EngagementId, escalated.Id, ApprovalDecisionTransitionTests.DecisionFor(escalated), CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Decided, result.Outcome);
    }

    /// <summary>S13.149: a request withdrawn because its run was terminated keeps its withdrawal through the store and refuses a later decision.</summary>
    [Fact]
    public async Task DecideAsync_WithdrawnRequest_IsRefusedAndKeepsWhoWithdrewIt()
    {
        var withdrawn = HitlFixtures.PendingRequest() with
        {
            Id = $"req-{Guid.NewGuid():N}",
            EngagementId = "eng-withdrawn",
            Status = ApprovalRequestStatus.Withdrawn,
            WithdrawnBy = "user:manager-1",
            WithdrawalReason = "Parked run, restarting.",
        };
        await store.UpsertAsync(withdrawn, CancellationToken.None);

        var result = await store.DecideAsync(withdrawn.EngagementId, withdrawn.Id, ApprovalDecisionTransitionTests.DecisionFor(withdrawn), CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Withdrawn, result.Request!.Status);
        Assert.Equal("user:manager-1", result.Request.WithdrawnBy);
        Assert.Equal("Parked run, restarting.", result.Request.WithdrawalReason);
        Assert.Null(result.Request.Decision);
    }

    /// <summary>S13.149: a run terminated at its gate withdraws the request under the ETag guard.</summary>
    [Theory]
    [InlineData("pending")]
    [InlineData("escalated")]
    public async Task WithdrawAsync_RequestAwaitingADecision_IsWithdrawnWithWhoAndWhy(string status)
    {
        var request = NewRequest($"eng-withdraw-{status}") with { Status = ApprovalRequestStatus.FromName(status) };
        await store.UpsertAsync(request, CancellationToken.None);

        var result = await store.WithdrawAsync(request.EngagementId, request.Id, "user:manager-1", "Parked run, restarting.", CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Applied, result.Outcome);
        var stored = (await ((IApprovalDocuments)store).ReadAsync(request.EngagementId, request.Id, CancellationToken.None))!.Request;
        Assert.Equal(ApprovalRequestStatus.Withdrawn, stored.Status);
        Assert.Equal("user:manager-1", stored.WithdrawnBy);
        Assert.Equal("Parked run, restarting.", stored.WithdrawalReason);
    }

    [Fact]
    public async Task WithdrawAsync_DecidedRequest_IsRefusedAndKeepsTheDecision()
    {
        var request = NewRequest("eng-withdraw-decided");
        await store.UpsertAsync(request, CancellationToken.None);
        var decision = ApprovalDecisionTransitionTests.DecisionFor(request);
        await store.DecideAsync(request.EngagementId, request.Id, decision, CancellationToken.None);

        var result = await store.WithdrawAsync(request.EngagementId, request.Id, "user:manager-1", "Too late.", CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Decided, result.Request!.Status);
        Assert.Equal(decision, result.Request.Decision);
        Assert.Null(result.Request.WithdrawnBy);
    }

    /// <summary>
    /// A decision and a withdrawal issued concurrently on one request never both land: exactly one is applied,
    /// the other is refused, and the stored record is the winner's. The emulator decides the interleaving, so
    /// this does not force an ETag conflict; <c>ApprovalLifecycleTests</c> drives each race direction through
    /// the retry path deterministically.
    /// </summary>
    [Fact]
    public async Task ConcurrentDecisionAndWithdrawal_NeverBothLand()
    {
        var request = NewRequest("eng-withdraw-race");
        await store.UpsertAsync(request, CancellationToken.None);

        var decideTask = store.DecideAsync(request.EngagementId, request.Id, ApprovalDecisionTransitionTests.DecisionFor(request), CancellationToken.None);
        var withdrawTask = store.WithdrawAsync(request.EngagementId, request.Id, "user:manager-1", "Parked run.", CancellationToken.None);
        await Task.WhenAll(decideTask, withdrawTask);
        var decide = await decideTask;
        var withdraw = await withdrawTask;

        var decided = decide.Outcome == ApprovalDecisionOutcome.Decided;
        var withdrawn = withdraw.Outcome == ApprovalTransitionOutcome.Applied;
        Assert.True(decided ^ withdrawn, $"decide={decide.Outcome.Name}, withdraw={withdraw.Outcome.Name}");
        var stored = (await ((IApprovalDocuments)store).ReadAsync(request.EngagementId, request.Id, CancellationToken.None))!.Request;
        Assert.Equal(decided ? ApprovalRequestStatus.Decided : ApprovalRequestStatus.Withdrawn, stored.Status);
        Assert.Equal(decided ? ApprovalTransitionOutcome.Refused : ApprovalTransitionOutcome.Applied, withdraw.Outcome);
        Assert.Equal(decided ? ApprovalDecisionOutcome.Decided : ApprovalDecisionOutcome.AlreadyDecided, decide.Outcome);
    }

    /// <summary>An escalation still running when the run is terminated must not re-open the withdrawn request.</summary>
    [Fact]
    public async Task EscalateAsync_WithdrawnRequest_IsRefusedAndStaysWithdrawn()
    {
        var request = NewRequest("eng-escalate-withdrawn");
        await store.UpsertAsync(request, CancellationToken.None);
        await store.WithdrawAsync(request.EngagementId, request.Id, "user:manager-1", "Parked run.", CancellationToken.None);

        var result = await store.EscalateAsync(request.EngagementId, request.Id, CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        var stored = (await ((IApprovalDocuments)store).ReadAsync(request.EngagementId, request.Id, CancellationToken.None))!.Request;
        Assert.Equal(ApprovalRequestStatus.Withdrawn, stored.Status);
    }

    /// <summary>The approver's own escalate decision re-opens the request for the real decision, as the unguarded write did.</summary>
    [Fact]
    public async Task EscalateAsync_AfterAnApproverEscalates_ReopensTheRequestForTheRealDecision()
    {
        var request = NewRequest("eng-escalate-reopen");
        await store.UpsertAsync(request, CancellationToken.None);
        var escalate = ApprovalDecisionTransitionTests.DecisionFor(request) with { Kind = DecisionKind.Escalate };
        await store.DecideAsync(request.EngagementId, request.Id, escalate, CancellationToken.None);

        var result = await store.EscalateAsync(request.EngagementId, request.Id, CancellationToken.None);
        var approve = await store.DecideAsync(request.EngagementId, request.Id, ApprovalDecisionTransitionTests.DecisionFor(request), CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Applied, result.Outcome);
        Assert.Null(result.Request!.Decision);
        Assert.Equal(ApprovalDecisionOutcome.Decided, approve.Outcome);
    }

    [Fact]
    public async Task EscalateAsync_ApprovedRequest_IsRefusedAndKeepsTheApproval()
    {
        var request = NewRequest("eng-escalate-approved");
        await store.UpsertAsync(request, CancellationToken.None);
        await store.DecideAsync(request.EngagementId, request.Id, ApprovalDecisionTransitionTests.DecisionFor(request), CancellationToken.None);

        var result = await store.EscalateAsync(request.EngagementId, request.Id, CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        Assert.Equal(DecisionKind.Approve, result.Request!.Decision!.Kind);
    }

    private static ApprovalRequest NewRequest(string engagementId) =>
        HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = engagementId };

    [Fact]
    public async Task DecideAsync_UnknownRequest_IsNotFound()
    {
        var ghost = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-ghost" };

        var result = await store.DecideAsync(ghost.EngagementId, ghost.Id, ApprovalDecisionTransitionTests.DecisionFor(ghost), CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.NotFound, result.Outcome);
        Assert.Null(result.Request);
    }
}
