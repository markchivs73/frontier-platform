namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// In-memory <see cref="IMappingProposalStore"/> with the Cosmos batch's semantics (ADR-PA32): the
/// allocation is atomic, the version id is create-only, and the proposal replace is conditional on
/// its ETag. <see cref="BeforeAllocate"/> lets a test slip another writer in between the version
/// scan and the batch — exactly the 409/412 a concurrent approval causes.
/// </summary>
internal sealed class FakeMappingProposalStore : IMappingProposalStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, RoleMapping> versions = [];
    private readonly Dictionary<string, StoredMappingProposal> proposals = new(StringComparer.Ordinal);
    private int etagCounter;

    /// <summary>The version <c>current</c> points at, if any.</summary>
    internal int? CurrentVersion { get; private set; }

    /// <summary>Runs inside <see cref="TryAllocateVersionAsync"/> before the conditional write.</summary>
    internal Func<Task>? BeforeAllocate { get; set; }

    /// <summary>Throws from <see cref="TryAllocateVersionAsync"/>, standing in for a store failure.</summary>
    internal Exception? AllocateThrows { get; set; }

    /// <summary>Every version this store was asked to make current, in order (a shadow version must never appear).</summary>
    internal List<int> MadeCurrent { get; } = [];

    /// <summary>Seeds an existing mapping version, optionally as the current pointer.</summary>
    internal FakeMappingProposalStore WithVersion(RoleMapping mapping, bool current = false)
    {
        versions[mapping.MappingVersion] = mapping;
        if (current)
        {
            CurrentVersion = mapping.MappingVersion;
        }

        return this;
    }

    /// <summary>Seeds a stored proposal and returns this store.</summary>
    internal FakeMappingProposalStore WithProposal(MappingChangeProposal proposal)
    {
        proposals[proposal.ProposalId] = new StoredMappingProposal(proposal, $"etag-{++etagCounter}");
        return this;
    }

    /// <summary>The stored mapping version, for assertions.</summary>
    internal RoleMapping? Version(int version) => versions.GetValueOrDefault(version);

    /// <summary>The stored proposal, for assertions.</summary>
    internal MappingChangeProposal? Proposal(string proposalId) => proposals.GetValueOrDefault(proposalId)?.Proposal;

    public Task<IReadOnlyList<int>> ListMappingVersionsAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<int>>([.. versions.Keys.Order()]);
        }
    }

    public Task<RoleMapping?> FindMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(versions.GetValueOrDefault(version));
        }
    }

    public Task<int?> FindCurrentVersionAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(CurrentVersion);
        }
    }

    public Task<StoredMappingProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(proposals.GetValueOrDefault(proposalId));
        }
    }

    public Task CreateProposalAsync(MappingChangeProposal proposal, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            proposals[proposal.ProposalId] = new StoredMappingProposal(proposal, $"etag-{++etagCounter}");
            return Task.CompletedTask;
        }
    }

    public Task<bool> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(TryReplace(proposal, expectedETag));
        }
    }

    public async Task<bool> TryAllocateVersionAsync(
        RoleMapping mapping, MappingChangeProposal proposal, string expectedETag, bool makeCurrent, CancellationToken cancellationToken)
    {
        if (BeforeAllocate is { } hook)
        {
            await hook();
        }

        if (AllocateThrows is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            // Create-only on {roleId}:v{n}: a version another approval already allocated is a conflict.
            if (versions.ContainsKey(mapping.MappingVersion) || !TryReplace(proposal, expectedETag))
            {
                return false;
            }

            versions[mapping.MappingVersion] = mapping;
            if (makeCurrent)
            {
                CurrentVersion = mapping.MappingVersion;
                MadeCurrent.Add(mapping.MappingVersion);
            }

            return true;
        }
    }

    public Task<StoredMappingProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(proposals.Values.FirstOrDefault(stored => stored.Proposal.State.IsAwaitingDecision));
        }
    }

    public Task<StoredMappingProposal?> FindProposalByLiveVersionAsync(string roleId, int version, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(proposals.Values.FirstOrDefault(stored =>
                (stored.Proposal.PromotedVersion ?? stored.Proposal.MappingVersion) == version));
        }
    }

    public Task<MappingProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var matching = proposals.Values
                .Select(stored => stored.Proposal)
                .Where(proposal => query.State is null || proposal.State == query.State)
                .OrderByDescending(proposal => proposal.ProposedAtUtc)
                .ToList();

            return Task.FromResult(new MappingProposalPage { Proposals = [.. matching.Take(query.PageSize)] });
        }
    }

    /// <summary>Replaces a proposal under its ETag, minting a new one; false when the token is stale.</summary>
    private bool TryReplace(MappingChangeProposal proposal, string expectedETag)
    {
        if (!proposals.TryGetValue(proposal.ProposalId, out var stored) || stored.ETag != expectedETag)
        {
            return false;
        }

        proposals[proposal.ProposalId] = new StoredMappingProposal(proposal, $"etag-{++etagCounter}");
        return true;
    }
}

/// <summary>Records every decision and compensation the service asks for (ADR-PA32's audit-first ordering).</summary>
internal sealed class FakeMappingDecisionRecorder : IMappingDecisionRecorder
{
    private int counter;

    /// <summary>Every decision recorded, in order.</summary>
    internal List<MappingGovernanceDecision> Decisions { get; } = [];

    /// <summary>Every compensation recorded, as (record id, reason).</summary>
    internal List<(string RecordId, string Reason)> Compensations { get; } = [];

    /// <summary>Makes <see cref="RecordAsync"/> throw, standing in for an unwritable audit record.</summary>
    internal Exception? RecordThrows { get; set; }

    public Task<string> RecordAsync(MappingGovernanceDecision decision, CancellationToken cancellationToken)
    {
        if (RecordThrows is { } failure)
        {
            throw failure;
        }

        Decisions.Add(decision);
        return Task.FromResult($"record-{++counter}");
    }

    public Task RecordCompensationAsync(MappingGovernanceDecision decision, string recordId, string failureReason, CancellationToken cancellationToken)
    {
        Compensations.Add((recordId, failureReason));
        return Task.CompletedTask;
    }
}

/// <summary>A clock the governance tests can assert stamped values against.</summary>
internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    /// <summary>The instant every governance decision in these tests is stamped with.</summary>
    internal static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
