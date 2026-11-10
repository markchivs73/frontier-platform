using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// Cosmos DB implementation of definition storage (workflow-definitions container, PK /workflowId).
/// Doc 13 §7 schema: draft, published versions, current pointer, proposals, validation reports.
/// </summary>
/// <remarks>
/// The draft-save and publish lost-update decisions are <see cref="DefinitionWriteRules"/>, unit-tested (S13.193).
/// The rest of this adapter still carries decision logic under its exclusion (proposal transitions, the catalogue
/// aggregation); narrowing it further is deferred, see the S13.193 PR.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Infrastructure: tested via integration tests against Cosmos emulator (DefinitionCompilerPhaseC_IntegrationTests); its save and publish conflict decisions are DefinitionWriteRules, unit-tested")]
public sealed class CosmosDefinitionStore : IDefinitionStore
{
    private readonly Container _container;

    public CosmosDefinitionStore(Container container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
    }

    public async Task<DefinitionDraftDocument> CreateDraftAsync(
        string workflowId,
        int baseVersion,
        DefinitionDraftDocument draft,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);
        ArgumentNullException.ThrowIfNull(draft);

        var doc = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = baseVersion,
            DraftRevision = draft.DraftRevision,
            Definition = draft.Definition,
            LastEditedBy = draft.LastEditedBy,
            LastEditedUtc = draft.LastEditedUtc
        };

        await _container.CreateItemAsync(doc, new PartitionKey(workflowId), cancellationToken: ct);
        return doc;
    }

    public async Task<DefinitionDraftDocument?> GetDraftAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);
        return (await ReadDraftAsync(workflowId, ct)).Document;
    }

    /// <summary>
    /// Reads the draft with the Cosmos <c>_etag</c> it was read at (GOV-03), so a save can replace
    /// exactly the version it compared. Null document when there is no draft.
    /// </summary>
    private async Task<(DefinitionDraftDocument? Document, string? ETag)> ReadDraftAsync(
        string workflowId,
        CancellationToken ct)
    {
        // S13.34: read the raw bytes, not a typed item. The stored schema version has to be probed
        // on the way past — MigratingWorkflowDefinitionConverter stamps the current version onto the
        // definition it returns, so once the document is materialised nothing can tell where the
        // draft came from, and its dropped keys surface as content errors on innocent nodes.
        using var response = await _container.ReadItemStreamAsync(
            $"{workflowId}:draft",
            new PartitionKey(workflowId),
            cancellationToken: ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, null);
        }

        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(response.Content);
        var json = await reader.ReadToEndAsync(ct);

        var document = JsonSerializer.Deserialize<DefinitionDraftDocument>(json, CanonicalProfile.Options)!;
        return (document with { StoredSchemaVersion = ProbeStoredSchemaVersion(json) }, response.Headers.ETag);
    }

    /// <summary>The <c>schema_version</c> the stored <c>definition</c> node declared, before migration (S13.34).</summary>
    private static string? ProbeStoredSchemaVersion(string documentJson)
    {
        using var parsed = JsonDocument.Parse(documentJson);

        return parsed.RootElement.TryGetProperty("definition", out var definition)
            ? MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion(definition.GetRawText())
            : null;
    }

    public async Task<SaveDraftResult> SaveDraftAsync(
        string workflowId,
        DefinitionDraftDocument draft,
        string expectedETag,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrEmpty(expectedETag)) throw new ArgumentException("ETag required", nameof(expectedETag));

        // The client-facing check is keyed on DraftRevision (ADR-CD2), the value the API exposes as
        // the client ETag. The write is then guarded by the Cosmos _etag of the document just
        // compared (GOV-03), so two saves on one revision cannot both pass the check and both win.
        var (current, currentETag) = await ReadDraftAsync(workflowId, ct);
        if (DefinitionWriteRules.IsStaleSave(current, expectedETag))
            return DefinitionWriteRules.Conflict(current);

        var doc = DraftToStore(workflowId, draft);
        if (current is null)
        {
            var created = await _container.UpsertItemAsync(doc, new PartitionKey(workflowId), cancellationToken: ct);
            return new SaveDraftResultSuccess(created.Resource);
        }

        return await ReplaceDraftAsync(doc, current, currentETag, ct);
    }

    /// <summary>
    /// Replaces the draft only if it is still at <paramref name="currentETag"/>. A lost race
    /// (another save landed after the read) is reported as a conflict with the winner's revision.
    /// </summary>
    private async Task<SaveDraftResult> ReplaceDraftAsync(
        DefinitionDraftDocument doc,
        DefinitionDraftDocument current,
        string? currentETag,
        CancellationToken ct)
    {
        try
        {
            var response = await _container.ReplaceItemAsync(
                doc, doc.Id, new PartitionKey(doc.WorkflowId), new ItemRequestOptions { IfMatchEtag = currentETag }, ct);
            return new SaveDraftResultSuccess(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            var winner = (await ReadDraftAsync(doc.WorkflowId, ct)).Document ?? current;
            return DefinitionWriteRules.Conflict(winner);
        }
    }

    /// <summary>The draft document as stored: fixed id and state, caller-supplied content.</summary>
    private static DefinitionDraftDocument DraftToStore(string workflowId, DefinitionDraftDocument draft) => new()
    {
        Id = $"{workflowId}:draft",
        WorkflowId = workflowId,
        State = "draft",
        BaseVersion = draft.BaseVersion,
        DraftRevision = draft.DraftRevision,
        Definition = draft.Definition,
        StoredSchemaVersion = draft.StoredSchemaVersion,
        LastEditedBy = draft.LastEditedBy,
        LastEditedUtc = draft.LastEditedUtc
    };

    public async Task<DefinitionVersionDocument> PublishVersionAsync(
        DefinitionVersionDocument versionDoc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(versionDoc);
        ValidateWorkflowId(versionDoc.WorkflowId);

        var doc = AsPublished(versionDoc);

        try
        {
            var response = await _container.CreateItemAsync(doc, new PartitionKey(versionDoc.WorkflowId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Version document already exists: DefinitionWriteRules decides re-run or refusal.
            var existing = await GetVersionAsync(versionDoc.WorkflowId, versionDoc.DefinitionVersion, ct);
            return DefinitionWriteRules.ResolvePublishConflict(existing, versionDoc);
        }
    }

    /// <summary>The version document as first written: <c>published</c>, with no successor and no retirement.</summary>
    private static DefinitionVersionDocument AsPublished(DefinitionVersionDocument versionDoc) => new()
    {
        Id = $"{versionDoc.WorkflowId}:v{versionDoc.DefinitionVersion}",
        WorkflowId = versionDoc.WorkflowId,
        State = "published",
        DefinitionVersion = versionDoc.DefinitionVersion,
        DefinitionHash = versionDoc.DefinitionHash,
        Definition = versionDoc.Definition,
        ProposedBy = versionDoc.ProposedBy,
        ApprovedBy = versionDoc.ApprovedBy,
        ProposedUtc = versionDoc.ProposedUtc,
        ApprovedUtc = versionDoc.ApprovedUtc,
        ValidationReportRef = versionDoc.ValidationReportRef,
        SupersededByVersion = null,
        Retirement = null,
        Title = versionDoc.Title,
        ChangeSummary = versionDoc.ChangeSummary
    };

    /// <inheritdoc />
    public Task<bool> RetireVersionAsync(
        string workflowId,
        int version,
        RetirementInfoDocument retirement,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(retirement);

        return PatchVersionStateAsync(
            workflowId,
            version,
            [PatchOperation.Set("/state", "retired"), PatchOperation.Set("/retirement", retirement)],
            "FROM c WHERE c.state != 'retired'",
            ct);
    }

    /// <inheritdoc />
    public async Task<bool> UnretireVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct) =>
        await PatchVersionStateAsync(
            workflowId,
            version,
            [PatchOperation.Set("/state", "superseded"), PatchOperation.Remove("/retirement")],
            "FROM c WHERE c.state = 'retired' AND IS_NUMBER(c.supersededByVersion)",
            ct)
        || await PatchVersionStateAsync(
            workflowId,
            version,
            [PatchOperation.Set("/state", "published"), PatchOperation.Remove("/retirement")],
            "FROM c WHERE c.state = 'retired' AND NOT IS_NUMBER(c.supersededByVersion)",
            ct);

    /// <inheritdoc />
    public async Task<bool> SupersedeVersionAsync(
        string workflowId,
        int version,
        int supersededByVersion,
        CancellationToken ct) =>
        await PatchVersionStateAsync(
            workflowId,
            version,
            [PatchOperation.Set("/state", "superseded"), PatchOperation.Set("/supersededByVersion", supersededByVersion)],
            "FROM c WHERE c.state = 'published' AND NOT IS_NUMBER(c.supersededByVersion)",
            ct)
        || await PatchVersionStateAsync(
            workflowId,
            version,
            [PatchOperation.Set("/supersededByVersion", supersededByVersion)],
            "FROM c WHERE c.state = 'retired' AND NOT IS_NUMBER(c.supersededByVersion)",
            ct);

    /// <summary>
    /// Patches only a version's state fields, guarded by <paramref name="precondition"/> so two racing
    /// transitions cannot both succeed. Missing version or failed precondition returns false.
    /// </summary>
    private async Task<bool> PatchVersionStateAsync(
        string workflowId,
        int version,
        IReadOnlyList<PatchOperation> operations,
        string precondition,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        using var response = await _container.PatchItemStreamAsync(
            $"{workflowId}:v{version}",
            new PartitionKey(workflowId),
            operations,
            new PatchItemRequestOptions { FilterPredicate = precondition },
            ct);

        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<DefinitionVersionDocument?> GetVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        try
        {
            var response = await _container.ReadItemAsync<DefinitionVersionDocument>(
                $"{workflowId}:v{version}",
                new PartitionKey(workflowId),
                cancellationToken: ct);
            return RecordedDefinitionHash.Fill(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DefinitionVersionDocument>> GetAllVersionsAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        var query = _container.GetItemQueryIterator<DefinitionVersionDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.workflowId = @workflowId AND c.state IN ('published', 'superseded', 'retired') ORDER BY c.definitionVersion DESC")
            .WithParameter("@workflowId", workflowId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        var versions = new List<DefinitionVersionDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            versions.AddRange(page.Select(RecordedDefinitionHash.Fill));
        }

        return versions.AsReadOnly();
    }

    public async Task<IReadOnlyList<DefinitionVersionDocument>> ListPublishedVersionsAsync(CancellationToken ct)
    {
        // Cross-partition fan-out (no PartitionKey) — a daily governance sweep, not a hot path;
        // same convention as ListPendingProposalsAsync (cosmos-conventions).
        var query = _container.GetItemQueryIterator<DefinitionVersionDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.state = @state")
                .WithParameter("@state", "published"));

        var versions = new List<DefinitionVersionDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            versions.AddRange(page.Select(RecordedDefinitionHash.Fill));
        }

        return versions.AsReadOnly();
    }

    public async Task<WorkflowHealthDocument> UpsertVersionHealthAsync(WorkflowHealthDocument health, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(health);
        ValidateWorkflowId(health.WorkflowId);

        var doc = health with { Id = $"{health.WorkflowId}:v{health.DefinitionVersion}:health" };
        var response = await _container.UpsertItemAsync(doc, new PartitionKey(health.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<IReadOnlyList<WorkflowHealthDocument>> ListVersionHealthAsync(string workflowId, CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        var query = _container.GetItemQueryIterator<WorkflowHealthDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.workflowId = @workflowId AND ENDSWITH(c.id, ':health')")
                .WithParameter("@workflowId", workflowId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        var health = new List<WorkflowHealthDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            health.AddRange(page);
        }

        return health.AsReadOnly();
    }

    public async Task<IReadOnlyList<WorkflowHealthDocument>> ListAllVersionHealthAsync(CancellationToken ct)
    {
        // Cross-partition read of a pre-computed projection (not a per-request aggregate) — the A1
        // attention chip + needs-attention worklist; same convention as ListAllWorkflowUsageAsync.
        var query = _container.GetItemQueryIterator<WorkflowHealthDocument>(
            new QueryDefinition("SELECT * FROM c WHERE ENDSWITH(c.id, ':health')"));

        var health = new List<WorkflowHealthDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            health.AddRange(page);
        }

        return health.AsReadOnly();
    }

    public async Task<WorkflowUsageDocument> UpsertWorkflowUsageAsync(WorkflowUsageDocument usage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ValidateWorkflowId(usage.WorkflowId);

        var doc = usage with { Id = $"{usage.WorkflowId}:usage" };
        var response = await _container.UpsertItemAsync(doc, new PartitionKey(usage.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<IReadOnlyList<WorkflowUsageDocument>> ListAllWorkflowUsageAsync(CancellationToken ct)
    {
        // Cross-partition read of a pre-computed projection (not a per-request aggregate) — the A1
        // catalogue join; same convention as ListPendingProposalsAsync (cosmos-conventions).
        var query = _container.GetItemQueryIterator<WorkflowUsageDocument>(
            new QueryDefinition("SELECT * FROM c WHERE ENDSWITH(c.id, ':usage')"));

        var usage = new List<WorkflowUsageDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            usage.AddRange(page);
        }

        return usage.AsReadOnly();
    }

    public async Task<CurrentVersionPointerDocument?> GetCurrentVersionPointerAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        try
        {
            var response = await _container.ReadItemAsync<CurrentVersionPointerDocument>(
                $"{workflowId}:current",
                new PartitionKey(workflowId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SetCurrentVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        await _container.UpsertItemAsync(PointerTo(workflowId, version), new PartitionKey(workflowId), cancellationToken: ct);
    }

    /// <summary>The current-version pointer naming <paramref name="version"/>.</summary>
    private static CurrentVersionPointerDocument PointerTo(string workflowId, int version) => new()
    {
        Id = $"{workflowId}:current",
        WorkflowId = workflowId,
        CurrentVersion = version
    };

    public async Task<PublishProposalDocument> CreateProposalAsync(
        PublishProposalDocument proposal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ValidateWorkflowId(proposal.WorkflowId);

        var doc = new PublishProposalDocument
        {
            Id = proposal.Id,
            WorkflowId = proposal.WorkflowId,
            DraftRevision = proposal.DraftRevision,
            ProposerId = proposal.ProposerId,
            ProposedAtUtc = proposal.ProposedAtUtc,
            ValidationReportRef = proposal.ValidationReportRef,
            State = ProposalState.InReview,
            ApproverNoteOrReason = null,
            Title = proposal.Title,
            ChangeSummary = proposal.ChangeSummary
        };

        var response = await _container.CreateItemAsync(doc, new PartitionKey(proposal.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<PublishProposalDocument?> GetProposalAsync(
        string proposalId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        try
        {
            var query = _container.GetItemQueryIterator<PublishProposalDocument>(
                new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", proposalId));

            if (query.HasMoreResults)
            {
                var page = await query.ReadNextAsync(ct);
                return page.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<bool> ApproveProposalAsync(
        string proposalId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        return TransitionProposalAsync(
            proposalId, ProposalState.Approved, proposal => proposal with { State = ProposalState.Approved }, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One transactional batch in the workflow's partition (S13.248): the proposal is replaced at the
    /// ETag it was read at, the version is created, and the pointer is upserted. Cosmos applies all
    /// three or none, so a lost race or a failure part-way leaves the proposal in review and no version.
    /// </remarks>
    public async Task<bool> ApproveAndPublishAsync(
        PublishProposalDocument proposal,
        DefinitionVersionDocument version,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(version);
        ValidateWorkflowId(proposal.WorkflowId);

        var partitionKey = new PartitionKey(proposal.WorkflowId);
        var read = await ReadProposalOrDefaultAsync(proposal.Id, partitionKey, ct);
        if (!DefinitionWriteRules.CanApprove(read?.Resource))
            return false;

        using var response = await _container.CreateTransactionalBatch(partitionKey)
            .ReplaceItem(proposal.Id, read!.Resource with { State = ProposalState.Approved }, new TransactionalBatchItemRequestOptions { IfMatchEtag = read.ETag })
            .CreateItem(AsPublished(version))
            .UpsertItem(PointerTo(version.WorkflowId, version.DefinitionVersion))
            .ExecuteAsync(ct);

        return DefinitionWriteRules.ApprovalLanded(
            response.StatusCode,
            () => $"The approve-and-publish batch for proposal '{proposal.Id}' (v{version.DefinitionVersion}) failed with status {(int)response.StatusCode}: {response.ErrorMessage}");
    }

    /// <summary>The proposal and its ETag, or <see langword="null"/> when it does not exist in <paramref name="partitionKey"/>.</summary>
    private async Task<ItemResponse<PublishProposalDocument>?> ReadProposalOrDefaultAsync(string proposalId, PartitionKey partitionKey, CancellationToken ct)
    {
        try
        {
            return await _container.ReadItemAsync<PublishProposalDocument>(proposalId, partitionKey, cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<bool> RejectProposalAsync(
        string proposalId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        return TransitionProposalAsync(
            proposalId,
            ProposalState.Rejected,
            proposal => proposal with { State = ProposalState.Rejected, ApproverNoteOrReason = reason },
            ct);
    }

    /// <summary>
    /// Moves a proposal to <paramref name="target"/>, replacing it only at the ETag it was read at
    /// (GOV-04), so two reviewers cannot both decide it. A missing proposal, a disallowed
    /// transition or a lost race returns false, which the lifecycle reports as a conflict.
    /// </summary>
    private async Task<bool> TransitionProposalAsync(
        string proposalId,
        ProposalState target,
        Func<PublishProposalDocument, PublishProposalDocument> apply,
        CancellationToken ct)
    {
        var located = await GetProposalAsync(proposalId, ct);
        if (located is null)
            return false;

        try
        {
            var partitionKey = new PartitionKey(located.WorkflowId);
            var read = await _container.ReadItemAsync<PublishProposalDocument>(proposalId, partitionKey, cancellationToken: ct);
            if (!read.Resource.State.CanTransitionTo(target))
                return false;

            await _container.ReplaceItemAsync(
                apply(read.Resource), proposalId, partitionKey, new ItemRequestOptions { IfMatchEtag = read.ETag }, ct);
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    public async Task<bool> WithdrawProposalAsync(
        string proposalId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        var proposal = await GetProposalAsync(proposalId, ct);
        if (proposal == null || !proposal.State.CanTransitionTo(ProposalState.Withdrawn))
            return false;

        try
        {
            await _container.DeleteItemAsync<PublishProposalDocument>(
                proposalId,
                new PartitionKey(proposal.WorkflowId),
                cancellationToken: ct);
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    public async Task<PublishProposalDocument?> GetActiveProposalAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        var query = _container.GetItemQueryIterator<PublishProposalDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.workflowId = @workflowId AND STARTSWITH(c.id, @prefix) AND c.state = @state")
                .WithParameter("@workflowId", workflowId)
                .WithParameter("@prefix", $"{workflowId}:proposal:")
                .WithParameter("@state", ProposalState.InReview.Name),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        if (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            return page.FirstOrDefault();
        }

        return null;
    }

    public async Task<IReadOnlyList<PublishProposalDocument>> ListPendingProposalsAsync(
        CancellationToken ct)
    {
        var query = _container.GetItemQueryIterator<PublishProposalDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE CONTAINS(c.id, @marker) AND c.state = @state ORDER BY c.proposedAtUtc DESC")
                .WithParameter("@marker", ":proposal:")
                .WithParameter("@state", ProposalState.InReview.Name));

        var results = new List<PublishProposalDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page);
        }

        return results;
    }

    public async Task DeleteDraftAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        await TryDeleteItemAsync<DefinitionDraftDocument>($"{workflowId}:draft", workflowId, ct);
        await TryDeleteItemAsync<ChatHistoryDocument>($"{workflowId}:chat-history", workflowId, ct);

        foreach (var turn in await GetAllDesignTurnsAsync(workflowId, ct))
            await TryDeleteItemAsync<DesignTurnDocument>(turn.Id, workflowId, ct);

        foreach (var testRun in await ListTestRunsAsync(workflowId, ct))
            await TryDeleteItemAsync<TestRunDocument>(testRun.Id, workflowId, ct);
    }

    /// <summary>Deletes an item by id, tolerating a concurrent/prior delete (S9.42's own draft/chat-history/turn/test-run cleanup is idempotent by construction).</summary>
    private async Task TryDeleteItemAsync<T>(string id, string workflowId, CancellationToken ct)
    {
        try
        {
            await _container.DeleteItemAsync<T>(id, new PartitionKey(workflowId), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — fine.
        }
    }

    public async Task<ValidationReportDocument> PersistValidationReportAsync(
        ValidationReportDocument report,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateWorkflowId(report.WorkflowId);

        var doc = new ValidationReportDocument
        {
            Id = $"{report.WorkflowId}:report:{report.DraftRevision}",
            WorkflowId = report.WorkflowId,
            DraftRevision = report.DraftRevision,
            ValidatedAtUtc = report.ValidatedAtUtc,
            Outcome = report.Outcome,
            Findings = report.Findings,
            ResourceVersions = report.ResourceVersions
        };

        var response = await _container.UpsertItemAsync(doc, new PartitionKey(report.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<ValidationReportDocument?> GetValidationReportAsync(
        string workflowId,
        string draftRevision,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);
        if (string.IsNullOrEmpty(draftRevision)) throw new ArgumentException("Draft revision required", nameof(draftRevision));

        try
        {
            var response = await _container.ReadItemAsync<ValidationReportDocument>(
                $"{workflowId}:report:{draftRevision}",
                new PartitionKey(workflowId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TestRunDocument> PersistTestRunAsync(
        TestRunDocument testRun,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(testRun);
        ValidateWorkflowId(testRun.WorkflowId);

        // The run is persisted as given, TTL aside. A field-by-field rebuild dropped each member it
        // didn't list: S9.85's Status (S9.89), then ADR-PA20's EngagementId, which left every run
        // unreconcilable. Mock-store unit tests cannot see this class, so the Cosmos round-trip
        // integration test pins that nothing is lost.
        var doc = testRun with { Ttl = TestRunDocument.SandboxRetentionSeconds };

        var response = await _container.UpsertItemAsync(doc, new PartitionKey(testRun.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<TestRunDocument?> GetTestRunAsync(
        string testRunId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(testRunId))
            return null;

        try
        {
            var query = _container.GetItemQueryIterator<TestRunDocument>(
                new QueryDefinition("SELECT * FROM c WHERE c.testRunId = @testRunId")
                .WithParameter("@testRunId", testRunId));

            if (query.HasMoreResults)
            {
                var page = await query.ReadNextAsync(ct);
                return page.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TestRunDocument>> ListTestRunsAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        var results = new List<TestRunDocument>();
        var query = _container.GetItemQueryIterator<TestRunDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.workflowId = @workflowId AND STARTSWITH(c.id, @prefix) ORDER BY c.startedAtUtc DESC")
                .WithParameter("@workflowId", workflowId)
                .WithParameter("@prefix", $"{workflowId}:testrun:"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page);
        }

        return results;
    }

    /// <summary>
    /// S9.86: cross-partition by design (no partition-key option) — the sweep and rollup need every
    /// active run regardless of workflow. Narrow by construction: only currently-active sandbox runs
    /// carry a non-terminal <c>status</c>, and the <c>:testrun:</c> id fragment keeps other document
    /// kinds out even if one ever grows a colliding <c>status</c> value.
    /// </summary>
    public async Task<IReadOnlyList<TestRunDocument>> ListActiveTestRunsAsync(CancellationToken ct)
    {
        var results = new List<TestRunDocument>();
        var query = _container.GetItemQueryIterator<TestRunDocument>(
            new QueryDefinition("SELECT * FROM c WHERE CONTAINS(c.id, \":testrun:\") AND c.status IN (@running, @paused) ORDER BY c.startedAtUtc DESC")
                .WithParameter("@running", TestRunStatus.Running)
                .WithParameter("@paused", TestRunStatus.PausedAtGate));

        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page);
        }

        return results;
    }

    public async Task<DesignTurnDocument> PersistDesignTurnAsync(
        DesignTurnDocument turn,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ValidateWorkflowId(turn.WorkflowId);

        // Upsert the turn as given: a field-by-field copy here once dropped Mentions (S13.224).
        var response = await _container.UpsertItemAsync(turn, new PartitionKey(turn.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<DesignTurnDocument?> GetDesignTurnAsync(
        string turnDocumentId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(turnDocumentId))
            return null;

        try
        {
            var query = _container.GetItemQueryIterator<DesignTurnDocument>(
                new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", turnDocumentId));

            if (query.HasMoreResults)
            {
                var page = await query.ReadNextAsync(ct);
                return page.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ChatHistoryDocument?> GetChatHistoryAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        try
        {
            var response = await _container.ReadItemAsync<ChatHistoryDocument>(
                $"{workflowId}:chat-history",
                new PartitionKey(workflowId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ChatHistoryDocument> CreateOrUpdateChatHistoryAsync(
        ChatHistoryDocument history,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(history);
        ValidateWorkflowId(history.WorkflowId);

        var doc = new ChatHistoryDocument
        {
            Id = $"{history.WorkflowId}:chat-history",
            WorkflowId = history.WorkflowId,
            DraftId = history.DraftId,
            NextTurnNumber = history.NextTurnNumber,
            LastMessageAtUtc = history.LastMessageAtUtc,
            TurnDocumentIds = history.TurnDocumentIds
        };

        var response = await _container.UpsertItemAsync(doc, new PartitionKey(history.WorkflowId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<IReadOnlyList<DesignTurnDocument>> GetAllDesignTurnsAsync(
        string workflowId,
        CancellationToken ct)
    {
        ValidateWorkflowId(workflowId);

        var query = _container.GetItemQueryIterator<DesignTurnDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.workflowId = @workflowId AND STARTSWITH(c.id, @prefix) ORDER BY c.turnNumber ASC")
            .WithParameter("@workflowId", workflowId)
            .WithParameter("@prefix", $"{workflowId}:turn:"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        var turns = new List<DesignTurnDocument>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            turns.AddRange(page);
        }

        return turns.AsReadOnly();
    }

    public async Task<WorkflowCataloguePage> ListWorkflowsAsync(
        string? engagementType,
        string? status,
        string? search,
        int offset,
        int limit,
        CancellationToken ct)
    {
        // Cross-partition query: fetch all draft docs (state="draft") and version docs
        // (state="published"/"superseded"/"retired"). Docs without a state field (chat-history,
        // current-pointer, turn docs) are excluded by the IN clause.
        var iterator = _container.GetItemQueryIterator<CatalogueRow>(
            new QueryDefinition(
                "SELECT c.workflowId, c.id, c.state, c.definitionVersion, c.approvedUtc, c.approvedBy, c.definition.name AS definitionName, " +
                "c.definition.engagement_type AS definitionEngagementType, c.definition.mode AS definitionMode " +
                "FROM c WHERE c.state IN ('draft', 'published', 'superseded', 'retired')"));

        var rows = new List<CatalogueRow>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            rows.AddRange(page);
        }

        // One entry per workflow: name, type and mode from the current published version, else the highest
        // row, with the draft's own name and type alongside (WorkflowCatalogueRules).
        var summaries = rows
            .GroupBy(r => r.WorkflowId, StringComparer.Ordinal)
            .Select(g => WorkflowCatalogueRules.Summarise(g.Key, g.ToList()))
            .ToList();

        // In-memory filters (Phase 1 — catalogue sizes are small).
        if (!string.IsNullOrWhiteSpace(engagementType))
            summaries = summaries.Where(s => string.Equals(s.EngagementType, engagementType, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(status))
            summaries = summaries.Where(s => string.Equals(s.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(search))
            summaries = summaries.Where(s =>
                s.WorkflowId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        var total = summaries.Count;
        var items = summaries.Skip(offset).Take(limit).ToList();
        return new WorkflowCataloguePage(items.AsReadOnly(), total);
    }

    private static void ValidateWorkflowId(string workflowId)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID required", nameof(workflowId));
    }
}
