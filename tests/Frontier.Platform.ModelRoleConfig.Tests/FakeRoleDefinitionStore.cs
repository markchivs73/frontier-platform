using System.Text.Json;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// In-memory <see cref="IRoleDefinitionStore"/> with the Cosmos batch's semantics (ADR-PA40): the
/// allocation is atomic, the version id is create-only, and the proposal replace is conditional on
/// its ETag. <see cref="BeforeAllocate"/> slips another writer in between the scan and the batch.
/// </summary>
internal sealed class FakeRoleDefinitionStore : IRoleDefinitionStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, RoleDefinition> versions = [];
    private readonly Dictionary<string, StoredRoleDefinitionProposal> proposals = new(StringComparer.Ordinal);
    private int etagCounter;

    /// <summary>The version the definition pointer names, if any.</summary>
    internal int? CurrentVersion { get; private set; }

    /// <summary>Runs inside <see cref="TryAllocateDefinitionAsync"/> before the conditional write.</summary>
    internal Func<Task>? BeforeAllocate { get; set; }

    /// <summary>Throws from <see cref="TryAllocateDefinitionAsync"/>, standing in for a store failure.</summary>
    internal Exception? AllocateThrows { get; set; }

    /// <summary>Throws from <see cref="CreateProposalAsync"/>, standing in for a store failure.</summary>
    internal Exception? CreateThrows { get; set; }

    /// <summary>Seeds a stored definition version, optionally as the current one.</summary>
    internal FakeRoleDefinitionStore WithDefinition(RoleDefinition definition, bool current = true)
    {
        versions[definition.DefinitionVersion!.Value] = definition;
        if (current)
        {
            CurrentVersion = definition.DefinitionVersion;
        }

        return this;
    }

    /// <summary>Seeds a stored proposal and returns this store.</summary>
    internal FakeRoleDefinitionStore WithProposal(RoleDefinitionProposal proposal)
    {
        proposals[proposal.ProposalId] = new StoredRoleDefinitionProposal(proposal, $"etag-{++etagCounter}");
        return this;
    }

    /// <summary>The stored version, for assertions.</summary>
    internal RoleDefinition? Version(int version) => versions.GetValueOrDefault(version);

    /// <summary>The stored proposal, for assertions.</summary>
    internal RoleDefinitionProposal? Proposal(string proposalId) => proposals.GetValueOrDefault(proposalId)?.Proposal;

    /// <summary>Every stored proposal, for assertions.</summary>
    internal IReadOnlyList<RoleDefinitionProposal> Proposals => [.. proposals.Values.Select(stored => stored.Proposal)];

    /// <summary>The stored proposal's current concurrency token.</summary>
    internal string? Token(string proposalId) => proposals.GetValueOrDefault(proposalId)?.ETag;

    public Task<RoleDefinition?> FindCurrentDefinitionAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(CurrentVersion is { } current ? versions[current] : null);
        }
    }

    public Task<IReadOnlyList<int>> ListDefinitionVersionsAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<int>>([.. versions.Keys.Order()]);
        }
    }

    public Task<StoredRoleDefinitionProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(proposals.Values.FirstOrDefault(stored => stored.Proposal.RoleId == roleId && stored.Proposal.State.IsAwaitingDecision));
        }
    }

    public Task<StoredRoleDefinitionProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var stored = proposals.GetValueOrDefault(proposalId);
            return Task.FromResult(stored is null ? null : stored with { Proposal = stored.Proposal with { ConcurrencyToken = stored.ETag } });
        }
    }

    public Task CreateProposalAsync(RoleDefinitionProposal proposal, CancellationToken cancellationToken)
    {
        if (CreateThrows is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            proposals[proposal.ProposalId] = new StoredRoleDefinitionProposal(proposal, $"etag-{++etagCounter}");
            return Task.CompletedTask;
        }
    }

    public Task<string?> TryReplaceProposalAsync(RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(TryReplace(proposal, expectedETag));
        }
    }

    public async Task<string?> TryAllocateDefinitionAsync(
        RoleDefinition definition, RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken)
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
            var version = definition.DefinitionVersion!.Value;
            if (versions.ContainsKey(version) || TryReplace(proposal, expectedETag) is not { } token)
            {
                return null;
            }

            versions[version] = definition;
            CurrentVersion = version;
            return token;
        }
    }

    public Task<RoleDefinitionProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var matching = proposals.Values
                .Where(stored => query.RoleId is null || stored.Proposal.RoleId == query.RoleId)
                .Where(stored => query.States is null || query.States.Count == 0 || query.States.Contains(stored.Proposal.State))
                .OrderByDescending(stored => stored.Proposal.ProposedAtUtc)
                .Take(query.PageSize);

            return Task.FromResult(new RoleDefinitionProposalPage
            {
                Proposals = [.. matching.Select(stored => stored.Proposal with { ConcurrencyToken = stored.ETag })],
            });
        }
    }

    private string? TryReplace(RoleDefinitionProposal proposal, string expectedETag)
    {
        if (!proposals.TryGetValue(proposal.ProposalId, out var stored) || stored.ETag != expectedETag)
        {
            return null;
        }

        var token = $"etag-{++etagCounter}";
        proposals[proposal.ProposalId] = new StoredRoleDefinitionProposal(proposal, token);
        return token;
    }
}

/// <summary>An in-memory <see cref="IContractSnapshotCatalog"/>: the consumer's registry, as the governance tests need it.</summary>
internal sealed class FakeContractCatalog : IContractSnapshotCatalog
{
    private readonly Dictionary<string, RegisteredContract> contracts = new(StringComparer.Ordinal);

    /// <summary>Every ref asked for, in order.</summary>
    internal List<string> Lookups { get; } = [];

    /// <summary>Registers a contract under <paramref name="contractRef"/>, filed under <paramref name="filedAs"/> when that differs.</summary>
    internal FakeContractCatalog With(string contractRef, bool retired = false, string? filedAs = null)
    {
        contracts[contractRef] = new RegisteredContract(Snapshot(filedAs ?? contractRef), retired);
        return this;
    }

    /// <summary>Marks a registered contract retired, as the registry would between propose and approve.</summary>
    internal void Retire(string contractRef) => contracts[contractRef] = contracts[contractRef] with { IsRetired = true };

    /// <summary>The hash this catalogue reports for <paramref name="contractRef"/>.</summary>
    internal static string HashOf(string contractRef) => "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contractRef)));

    public Task<RegisteredContract?> FindAsync(string contractRef, CancellationToken cancellationToken)
    {
        Lookups.Add(contractRef);
        return Task.FromResult(contracts.GetValueOrDefault(contractRef));
    }

    private static ContractSnapshot Snapshot(string contractRef)
    {
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        return new ContractSnapshot { ContractRef = contractRef, Schema = schema.RootElement.Clone(), Rules = [], Hash = HashOf(contractRef) };
    }
}
