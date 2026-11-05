using System.Net;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>
/// Unit tests for <see cref="CosmosBudgetLedger"/>'s decisions (S13.193): the read-accumulate-write retry over a
/// lost ETag race, and which ledger document answers each <see cref="BudgetScopeKind"/>. The SDK half is
/// <see cref="CosmosBudgetLedgerDocuments"/>, covered against the emulator.
/// </summary>
public sealed class CosmosBudgetLedgerTests
{
    private static readonly UsageRecord Usage = BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m);

    [Fact]
    public async Task RecordUsageAsync_NoLedgerYet_CreatesTheFirstDocument()
    {
        var documents = new FakeLedgerDocuments();

        await Ledger(documents).RecordUsageAsync(Usage, CancellationToken.None);

        var created = Assert.Single(documents.Created);
        Assert.Equal("eng-1:ledger", created.Id);
        Assert.Equal(150, created.TotalInputTokens + created.TotalOutputTokens);
        Assert.Empty(documents.Replaced);
    }

    [Fact]
    public async Task RecordUsageAsync_ExistingLedger_ReplacesUnderTheReadETag()
    {
        var documents = new FakeLedgerDocuments { Stored = (LedgerWith(200), "etag-7") };

        await Ledger(documents).RecordUsageAsync(Usage, CancellationToken.None);

        var (replaced, etag) = Assert.Single(documents.Replaced);
        Assert.Equal("etag-7", etag);
        Assert.Equal(350, replaced.TotalInputTokens + replaced.TotalOutputTokens);
        Assert.Empty(documents.Created);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task RecordUsageAsync_LostRace_BacksOffWithinTheCeilingAndRetriesFromTheRead(HttpStatusCode lostRace)
    {
        var ceilings = new List<int>();
        var documents = new FakeLedgerDocuments { FailingWrites = 2, FailureStatus = lostRace };

        await new CosmosBudgetLedger(documents, ceiling => { ceilings.Add(ceiling); return 0; }).RecordUsageAsync(Usage, CancellationToken.None);

        Assert.Equal(3, documents.Reads);
        Assert.Equal([BudgetLedgerWriteConflict.BaseBackoffMilliseconds, BudgetLedgerWriteConflict.BaseBackoffMilliseconds * 2], ceilings);
        Assert.Single(documents.Created);
    }

    [Fact]
    public async Task RecordUsageAsync_LosesEveryAttempt_RethrowsTheLastConflictAfterTheCap()
    {
        var documents = new FakeLedgerDocuments { FailingWrites = int.MaxValue };

        var ex = await Assert.ThrowsAsync<CosmosException>(() => Ledger(documents).RecordUsageAsync(Usage, CancellationToken.None));

        Assert.Equal(HttpStatusCode.PreconditionFailed, ex.StatusCode);
        Assert.Equal(CosmosBudgetLedger.MaxWriteAttempts, documents.Reads);
    }

    [Fact]
    public async Task RecordUsageAsync_NonConflictFailure_IsNotRetried()
    {
        var documents = new FakeLedgerDocuments { FailingWrites = int.MaxValue, FailureStatus = HttpStatusCode.TooManyRequests };

        await Assert.ThrowsAsync<CosmosException>(() => Ledger(documents).RecordUsageAsync(Usage, CancellationToken.None));

        Assert.Equal(1, documents.Reads);
    }

    [Fact]
    public async Task RecordUsageAsync_NullUsage_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ledger(new FakeLedgerDocuments()).RecordUsageAsync(null!, CancellationToken.None));

    [Fact]
    public async Task GetSnapshotAsync_Engagement_ReturnsTheLedgerTotals()
    {
        var documents = new FakeLedgerDocuments { Stored = (LedgerWith(200), "etag-1") };
        var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1");

        var snapshot = await Ledger(documents).GetSnapshotAsync(scope, CancellationToken.None);

        Assert.Equal(new BudgetSnapshot(scope, 200, 0.5m, "USD", 3), snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_EngagementWithNoLedger_IsEmpty()
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1");

        Assert.Equal(CosmosBudgetLedger.Empty(scope), await Ledger(new FakeLedgerDocuments()).GetSnapshotAsync(scope, CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_Execution_ReturnsTheFirstDocumentHoldingTheExecution()
    {
        var unrelated = LedgerWith(1) with { ExecutionSnapshots = null };
        var holder = LedgerWith(1) with { ExecutionSnapshots = new() { ["exec-1"] = new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 2, DateTime.UnixEpoch) } };
        var documents = new FakeLedgerDocuments { ByExecution = [unrelated, holder] };
        var scope = new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1");

        var snapshot = await Ledger(documents).GetSnapshotAsync(scope, CancellationToken.None);

        Assert.Equal(new BudgetSnapshot(scope, 150, 0.0012m, "USD", 2), snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_ExecutionNotRecorded_IsEmpty()
    {
        var other = LedgerWith(1) with { ExecutionSnapshots = new() { ["exec-2"] = new ExecutionLedgerSnapshot("exec-2", 1, 0m, "USD", 1, DateTime.UnixEpoch) } };
        var scope = new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1");

        var snapshot = await Ledger(new FakeLedgerDocuments { ByExecution = [other] }).GetSnapshotAsync(scope, CancellationToken.None);

        Assert.Equal(CosmosBudgetLedger.Empty(scope), snapshot);
    }

    [Theory]
    [InlineData(BudgetScopeKind.Invocation)]
    [InlineData(BudgetScopeKind.Fleet)]
    public async Task GetSnapshotAsync_ScopesTheLedgerDoesNotHold_AreEmptyWithoutReading(BudgetScopeKind kind)
    {
        var documents = new FakeLedgerDocuments();
        var scope = new BudgetScopeRef(kind, "x");

        Assert.Equal(CosmosBudgetLedger.Empty(scope), await Ledger(documents).GetSnapshotAsync(scope, CancellationToken.None));
        Assert.Equal(0, documents.Reads);
    }

    [Fact]
    public async Task GetSnapshotAsync_UnknownKind_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Ledger(new FakeLedgerDocuments()).GetSnapshotAsync(new BudgetScopeRef((BudgetScopeKind)99, "x"), CancellationToken.None));

    [Fact]
    public async Task GetSnapshotAsync_NullScope_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ledger(new FakeLedgerDocuments()).GetSnapshotAsync(null!, CancellationToken.None));

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new CosmosBudgetLedger(null!, _ => 0));
        Assert.Throws<ArgumentNullException>(() => new CosmosBudgetLedger(new FakeLedgerDocuments(), null!));
        Assert.Throws<ArgumentNullException>(() => new CosmosBudgetLedger((Container)null!));
    }

    [Fact]
    public void FullJitterMilliseconds_StaysWithinZeroAndTheCeiling()
    {
        var draws = Enumerable.Range(0, 200).Select(_ => CosmosBudgetLedger.FullJitterMilliseconds(3)).ToList();

        Assert.All(draws, draw => Assert.InRange(draw, 0, 3));
    }

    private static CosmosBudgetLedger Ledger(FakeLedgerDocuments documents) => new(documents, _ => 0);

    private static BudgetLedgerDocument LedgerWith(long tokens) => new()
    {
        PartitionKey = "eng-1",
        Id = "eng-1:ledger",
        EngagementId = "eng-1",
        TotalInputTokens = tokens,
        TotalOutputTokens = 0,
        TotalCost = 0.5m,
        Currency = "USD",
        InvocationCount = 3,
    };

    /// <summary>In-memory ledger documents whose first <see cref="FailingWrites"/> writes lose a race.</summary>
    private sealed class FakeLedgerDocuments : IBudgetLedgerDocuments
    {
        private int writes;

        public (BudgetLedgerDocument? Document, string? ETag) Stored { get; init; }

        public IReadOnlyList<BudgetLedgerDocument> ByExecution { get; init; } = [];

        public int FailingWrites { get; init; }

        public HttpStatusCode FailureStatus { get; init; } = HttpStatusCode.PreconditionFailed;

        public int Reads { get; private set; }

        public List<BudgetLedgerDocument> Created { get; } = [];

        public List<(BudgetLedgerDocument Document, string? ETag)> Replaced { get; } = [];

        public Task<(BudgetLedgerDocument? Document, string? ETag)> ReadAsync(string engagementId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Stored);
        }

        public Task CreateAsync(BudgetLedgerDocument document, CancellationToken cancellationToken)
        {
            ThrowIfLosing();
            Created.Add(document);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(BudgetLedgerDocument document, string? etag, CancellationToken cancellationToken)
        {
            ThrowIfLosing();
            Replaced.Add((document, etag));
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BudgetLedgerDocument> FindByExecutionAsync(string executionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var document in ByExecution)
            {
                await Task.Yield();
                yield return document;
            }
        }

        private void ThrowIfLosing()
        {
            if (++writes <= FailingWrites)
            {
                throw new CosmosException("lost the race", FailureStatus, 0, "activity", 0);
            }
        }
    }
}
