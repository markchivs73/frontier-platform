using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.328 (a2): at every one of the eight places the two governance services compensate a decision record, a recorder
/// whose compensation does not land — throwing synchronously or returning a faulted task — never replaces the change's own
/// outcome. The failure is logged at Critical with its exception, record id and event type. The trail itself stays wrong
/// (an uncompensated record) until S13.328 (b)/(c); these tests prove containment, not repair.
/// </summary>
public sealed class CompensationContainmentTests
{
    private static readonly InvalidOperationException Lost = new("audit append failed");

    public static TheoryData<bool> HowItFails => new() { true, false };

    // ---- MappingGovernanceService ----

    /// <summary>The approve's store write fails (TryLandAsync's catch): the store's exception still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Mapping_ApproveStoreThrows_TheStoresExceptionSurfaces(bool synchronously)
    {
        var store = new FakeMappingProposalStore().WithProposal(MappingProposalSamples.Pending());
        store.AllocateThrows = new InvalidOperationException("cosmos down");
        var (recorder, logger) = Failing(synchronously);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MappingProposalSamples.Service(store, recorder, logger: logger.Mapping).ApproveAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "eval accepted", null, CancellationToken.None));

        Assert.Equal("cosmos down", thrown.Message);
        AssertOneCriticalNaming(logger.Mapping, recorder, "record-1", beforeRetry: false);
    }

    /// <summary>A concurrent approval took the version (landed is null): the retry still runs and its record lands.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Mapping_ApproveLostTheVersion_TheRetrysRecordLands(bool synchronously)
    {
        var store = new FakeMappingProposalStore().WithVersion(MappingProposalSamples.FleetV1, current: true).WithProposal(MappingProposalSamples.Pending());
        var raced = false;
        store.BeforeAllocate = () =>
        {
            if (!raced)
            {
                raced = true;
                store.WithVersion(MappingProposalSamples.FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary });
            }

            return Task.CompletedTask;
        };
        var (recorder, logger) = Failing(synchronously);

        var approved = await MappingProposalSamples.Service(store, recorder, logger: logger.Mapping)
            .ApproveAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "eval accepted", null, CancellationToken.None);

        Assert.Equal(3, approved.MappingVersion);
        Assert.Equal(2, recorder.Decisions.Count);   // the retry's RecordAsync landed
        AssertOneCriticalNaming(logger.Mapping, recorder, "record-1", beforeRetry: true);
    }

    /// <summary>Every attempt loses the proposal's ETag while every compensation fails: the exhaustion still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Mapping_EveryAttemptLosesAndEveryCompensationFails_TheExhaustionSurfaces(bool synchronously)
    {
        var store = new FakeMappingProposalStore().WithVersion(MappingProposalSamples.FleetV1, current: true).WithProposal(MappingProposalSamples.Pending());
        store.BeforeAllocate = () => store.CreateProposalAsync(MappingProposalSamples.Pending(), CancellationToken.None);
        var (recorder, logger) = Failing(synchronously);

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            MappingProposalSamples.Service(store, recorder, logger: logger.Mapping).ApproveAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "eval accepted", null, CancellationToken.None));

        Assert.Equal(MappingProposalSamples.FastRetry.DecisionMaxAttempts, recorder.CompensationAttempts.Count);
        Assert.All(logger.Mapping.Entries, e => Assert.Equal(LogLevel.Critical, e.Level));
        Assert.Equal(recorder.CompensationAttempts.Count, logger.Mapping.Entries.Count);
    }

    /// <summary>A rollback's pointer write fails (RecordThenAsync's catch): the writer's exception still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Mapping_RollbackPointerWriteFails_TheWritersExceptionSurfaces(bool synchronously)
    {
        var store = new FakeMappingProposalStore()
            .WithVersion(MappingProposalSamples.FleetV1)
            .WithVersion(MappingProposalSamples.FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true);
        var writer = new FakeRoleMappingWriter { Throws = new InvalidOperationException("pointer write failed") };
        var (recorder, logger) = Failing(synchronously);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MappingProposalSamples.Service(store, recorder, writer, logger: logger.Mapping).RollbackToVersionAsync(MappingProposalSamples.RoleId, 1, MappingProposalSamples.Approver, "revert", CancellationToken.None));

        Assert.Equal("pointer write failed", thrown.Message);
        AssertOneCriticalNaming(logger.Mapping, recorder, "record-1", beforeRetry: false);
    }

    // ---- RoleDefinitionGovernanceService ----

    /// <summary>A proposal's write fails after its record (RecordThenAsync's catch): the store's exception still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Definition_ProposeWriteFails_TheStoresExceptionSurfaces(bool synchronously)
    {
        var store = new FakeRoleDefinitionStore { CreateThrows = new InvalidOperationException("cosmos down") };
        var (recorder, logger) = Failing(synchronously);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RoleDefinitionProposalSamples.Service(store, recorder, logger: logger.Definition).ProposeAsync(RoleDefinitionProposalSamples.AgentDefinition(), RoleDefinitionProposalSamples.Proposer, RoleDefinitionProposalSamples.Reason, CancellationToken.None));

        Assert.Equal("cosmos down", thrown.Message);
        AssertOneCriticalNaming(logger.Definition, recorder, "record-1", beforeRetry: false);
    }

    /// <summary>The approve's batch fails (TryLandAsync's catch): the batch's exception still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Definition_ApproveBatchThrows_TheBatchsExceptionSurfaces(bool synchronously)
    {
        var store = new FakeRoleDefinitionStore { AllocateThrows = new InvalidOperationException("batch failed") }.WithProposal(RoleDefinitionProposalSamples.Pending());
        var (recorder, logger) = Failing(synchronously);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RoleDefinitionProposalSamples.Service(store, recorder, logger: logger.Definition).ApproveAsync(RoleDefinitionProposalSamples.AgentRoleId, "d-1", RoleDefinitionProposalSamples.Approver, "ok", null, CancellationToken.None));

        Assert.Equal("batch failed", thrown.Message);
        AssertOneCriticalNaming(logger.Definition, recorder, "record-1", beforeRetry: false);
    }

    /// <summary>A concurrent writer took the version (landed is null): the retry still runs and its record lands.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Definition_ApproveLostTheVersion_TheRetrysRecordLands(bool synchronously)
    {
        var store = new FakeRoleDefinitionStore().WithProposal(RoleDefinitionProposalSamples.Pending());
        var raced = false;
        store.BeforeAllocate = () =>
        {
            if (!raced)
            {
                raced = true;
                store.WithDefinition(RoleDefinitionSamples.AgentBacked(RoleDefinitionProposalSamples.AgentRoleId) with { DefinitionVersion = 1 }, current: false);
            }

            return Task.CompletedTask;
        };
        var (recorder, logger) = Failing(synchronously);

        var approved = await RoleDefinitionProposalSamples.Service(store, recorder, logger: logger.Definition)
            .ApproveAsync(RoleDefinitionProposalSamples.AgentRoleId, "d-1", RoleDefinitionProposalSamples.Approver, "ok", null, CancellationToken.None);

        Assert.Equal(2, approved.DefinitionVersion);
        Assert.Equal(2, recorder.Decisions.Count);   // the retry's RecordAsync landed
        AssertOneCriticalNaming(logger.Definition, recorder, "record-1", beforeRetry: true);
    }

    /// <summary>Every attempt loses while every compensation fails: the exhaustion still surfaces.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Definition_EveryAttemptLosesAndEveryCompensationFails_TheExhaustionSurfaces(bool synchronously)
    {
        var store = new FakeRoleDefinitionStore().WithProposal(RoleDefinitionProposalSamples.Pending());
        store.BeforeAllocate = () => store.CreateProposalAsync(RoleDefinitionProposalSamples.Pending(), CancellationToken.None);
        var (recorder, logger) = Failing(synchronously);

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            RoleDefinitionProposalSamples.Service(store, recorder, logger: logger.Definition).ApproveAsync(RoleDefinitionProposalSamples.AgentRoleId, "d-1", RoleDefinitionProposalSamples.Approver, "ok", null, CancellationToken.None));

        Assert.Equal(RoleDefinitionProposalSamples.FastRetry.DecisionMaxAttempts, recorder.CompensationAttempts.Count);
        Assert.All(logger.Definition.Entries, e => Assert.Equal(LogLevel.Critical, e.Level));
        Assert.Equal(recorder.CompensationAttempts.Count, logger.Definition.Entries.Count);
    }

    // ---- the decide race sites (reject / withdraw) ----

    /// <summary>A mapping reject loses its first replace to another decision (DecideAsync's race): the retry lands.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Mapping_RejectLosesOnce_TheRetrysRecordLands(bool synchronously)
    {
        var store = new FakeMappingProposalStore().WithProposal(MappingProposalSamples.Pending());
        store.ReplacesToLose = 1;
        var (recorder, logger) = Failing(synchronously);

        var rejected = await MappingProposalSamples.Service(store, recorder, logger: logger.Mapping)
            .RejectAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "no", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
        Assert.Equal(2, recorder.Decisions.Count);   // the retry's RecordAsync landed
        AssertOneCriticalNaming(logger.Mapping, recorder, "record-1", beforeRetry: true);
    }

    /// <summary>A definition withdraw loses its first replace to another decision (DecideAsync's race): the retry lands.</summary>
    [Theory]
    [MemberData(nameof(HowItFails))]
    public async Task Definition_WithdrawLosesOnce_TheRetrysRecordLands(bool synchronously)
    {
        var store = new FakeRoleDefinitionStore().WithProposal(RoleDefinitionProposalSamples.Pending());
        store.ReplacesToLose = 1;
        var (recorder, logger) = Failing(synchronously);

        var withdrawn = await RoleDefinitionProposalSamples.Service(store, recorder, logger: logger.Definition)
            .WithdrawAsync(RoleDefinitionProposalSamples.AgentRoleId, "d-1", RoleDefinitionProposalSamples.Proposer, "changed my mind", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Withdrawn, withdrawn.State);
        Assert.Equal(2, recorder.Decisions.Count);   // the retry's RecordAsync landed
        AssertOneCriticalNaming(logger.Definition, recorder, "record-1", beforeRetry: true);
    }

    /// <summary>Every compensation is attempted uncancellably: the services hand the recorder <see cref="CancellationToken.None"/>.</summary>
    [Fact]
    public async Task EveryCompensation_IsGivenAnUncancellableToken()
    {
        var store = new FakeMappingProposalStore().WithProposal(MappingProposalSamples.Pending());
        store.ReplacesToLose = 1;
        var recorder = new FakeMappingDecisionRecorder();
        using var caller = new CancellationTokenSource();

        await MappingProposalSamples.Service(store, recorder).RejectAsync(MappingProposalSamples.RoleId, "p-1", MappingProposalSamples.Approver, "no", null, caller.Token);

        Assert.Equal([false], recorder.CompensationTokensCancellable);
    }

    // ---- the guard itself ----

    [Fact]
    public async Task Guard_CompensationLands_LogsNothing()
    {
        var recorder = new FakeMappingDecisionRecorder();
        var logger = new RecordingLogger<CompensationContainmentTests>();

        await CompensationGuard.TryCompensateAsync(recorder, Decision(), "record-9", "store down", logger);

        Assert.Equal(("record-9", "store down"), Assert.Single(recorder.Compensations));
        Assert.Empty(logger.Entries);
    }

    /// <summary>No caller token rides on a compensation, so a cancellation from the recorder is an SDK timeout: contained and logged.</summary>
    [Fact]
    public async Task Guard_RecorderCancels_IsContainedAndLoggedWithItsException()
    {
        var timeout = new OperationCanceledException("sdk timeout");
        var recorder = new FakeMappingDecisionRecorder { CompensationThrows = timeout };
        var logger = new RecordingLogger<CompensationContainmentTests>();

        await CompensationGuard.TryCompensateAsync(recorder, Decision(), "record-9", "store down", logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Critical, timeout), (entry.Level, entry.Exception));
    }

    /// <summary>
    /// Only the guard calls the recorder's compensation: a source scan of the library, so a new call site cannot skip
    /// containment (S13.328 (a2)).
    /// </summary>
    [Fact]
    public void OnlyTheGuard_CallsRecordCompensationAsync()
    {
        var library = Path.Combine(RepositoryRoot(), "src", "Frontier.Platform.ModelRoleConfig");
        var callers = Directory.EnumerateFiles(library, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(".RecordCompensationAsync(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["CompensationGuard.cs"], callers);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    private static (FakeMappingDecisionRecorder Recorder, Loggers Logger) Failing(bool synchronously) =>
        (synchronously ? new FakeMappingDecisionRecorder { CompensationThrows = Lost } : new FakeMappingDecisionRecorder { CompensationFaults = Lost }, new Loggers());

    /// <summary>One Critical entry carrying the lost exception, naming the record and its event type, worded for the site.</summary>
    private static void AssertOneCriticalNaming<T>(RecordingLogger<T> logger, FakeMappingDecisionRecorder recorder, string recordId, bool beforeRetry)
    {
        var attempt = Assert.Single(recorder.CompensationAttempts);
        Assert.Equal(recordId, attempt.RecordId);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Critical, (Exception)Lost), (entry.Level, entry.Exception!));
        Assert.Contains(recordId, entry.Message, StringComparison.Ordinal);
        Assert.Contains(attempt.EventType, entry.Message, StringComparison.Ordinal);
        Assert.Equal(beforeRetry, entry.Message.Contains("beside the retry's record", StringComparison.Ordinal));
    }

    private static MappingGovernanceDecision Decision() => new()
    {
        EventType = MappingGovernanceEventTypes.Proposed,
        RoleId = "analyst",
        Actor = "user:oid-a",
        Reason = "r",
        OccurredAtUtc = FixedTimeProvider.Now,
    };

    private sealed class Loggers
    {
        public RecordingLogger<MappingGovernanceService> Mapping { get; } = new();
        public RecordingLogger<RoleDefinitionGovernanceService> Definition { get; } = new();
    }
}
