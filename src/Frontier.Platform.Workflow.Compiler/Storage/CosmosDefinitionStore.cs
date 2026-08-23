using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;

using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// Cosmos DB implementation of definition storage (workflow-definitions container, PK /workflowId).
/// Doc 13 §7 schema: draft, published versions, current pointer, proposals, validation reports.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Infrastructure: tested via integration tests against Cosmos emulator (DefinitionCompilerPhaseC_IntegrationTests)")]
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

        try
        {
            var response = await _container.ReadItemAsync<DefinitionDraftDocument>(
                $"{workflowId}:draft",
                new PartitionKey(workflowId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
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

        // Optimistic concurrency is keyed on DraftRevision (ADR-CD2) — the value the API exposes as
        // the client ETag — not Cosmos's native _etag. (Feeding DraftRevision into IfMatchEtag always
        // precondition-fails, since Cosmos compares it against _etag.) Compare the stored revision,
        // then upsert.
        var current = await GetDraftAsync(workflowId, ct);
        if (current is not null && !string.Equals(current.DraftRevision, expectedETag, StringComparison.Ordinal))
            return new SaveDraftResultConflict("unknown", current.DraftRevision, current);

        var doc = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = draft.BaseVersion,
            DraftRevision = draft.DraftRevision,
            Definition = draft.Definition,
            LastEditedBy = draft.LastEditedBy,
            LastEditedUtc = draft.LastEditedUtc
        };

        var response = await _container.UpsertItemAsync(doc, new PartitionKey(workflowId), cancellationToken: ct);
        return new SaveDraftResultSuccess(response.Resource);
    }

    public async Task<DefinitionVersionDocument> PublishVersionAsync(
        DefinitionVersionDocument versionDoc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(versionDoc);
        ValidateWorkflowId(versionDoc.WorkflowId);

        var doc = new DefinitionVersionDocument
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
            Retirement = null
        };

        try
        {
            var response = await _container.CreateItemAsync(doc, new PartitionKey(versionDoc.WorkflowId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Version document already exists — this is an idempotent re-run (e.g. a previous
            // attempt created the document but failed before updating the current-version pointer).
            // Re-use the existing document if the definition hash matches; reject otherwise.
            var existing = await GetVersionAsync(versionDoc.WorkflowId, versionDoc.DefinitionVersion, ct);
            if (existing is not null && existing.DefinitionHash == versionDoc.DefinitionHash)
                return existing;
            throw new InvalidOperationException(
                $"Version {versionDoc.DefinitionVersion} already exists with a different definition hash — cannot publish.");
        }
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
            return response.Resource;
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
            versions.AddRange(page);
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
            versions.AddRange(page);
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

        var doc = new CurrentVersionPointerDocument
        {
            Id = $"{workflowId}:current",
            WorkflowId = workflowId,
            CurrentVersion = version
        };

        await _container.UpsertItemAsync(doc, new PartitionKey(workflowId), cancellationToken: ct);
    }

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
            ApproverNoteOrReason = null
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

    public async Task<bool> ApproveProposalAsync(
        string proposalId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        var proposal = await GetProposalAsync(proposalId, ct);
        if (proposal == null || !proposal.State.CanTransitionTo(ProposalState.Approved))
            return false;

        var updated = proposal with { State = ProposalState.Approved };
        try
        {
            await _container.UpsertItemAsync(updated, new PartitionKey(proposal.WorkflowId), cancellationToken: ct);
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    public async Task<bool> RejectProposalAsync(
        string proposalId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));

        var proposal = await GetProposalAsync(proposalId, ct);
        if (proposal == null || !proposal.State.CanTransitionTo(ProposalState.Rejected))
            return false;

        var updated = proposal with
        {
            State = ProposalState.Rejected,
            ApproverNoteOrReason = reason
        };

        try
        {
            await _container.UpsertItemAsync(updated, new PartitionKey(proposal.WorkflowId), cancellationToken: ct);
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

        var doc = new TestRunDocument
        {
            Id = testRun.Id,
            WorkflowId = testRun.WorkflowId,
            TestRunId = testRun.TestRunId,
            DraftRevision = testRun.DraftRevision,
            StartedAtUtc = testRun.StartedAtUtc,
            CompletedAtUtc = testRun.CompletedAtUtc,
            GateMode = testRun.GateMode,
            // S9.89 live-E2E find: this field-by-field rebuild silently dropped the S9.85 Status —
            // every persisted run read as legacy/terminal-by-CompletedAtUtc and the active-runs
            // query matched nothing. Mock-store unit tests can't see this class (real-I/O,
            // coverage-excluded); the live loop caught it.
            Status = testRun.Status,
            Success = testRun.Success,
            NodeSteps = testRun.NodeSteps,
            FailureNodeId = testRun.FailureNodeId,
            ValidatorFindings = testRun.ValidatorFindings,
            CostMetrics = testRun.CostMetrics,
            GateDecisions = testRun.GateDecisions,
            ErrorMessage = testRun.ErrorMessage,
            PausedAtGateId = testRun.PausedAtGateId,
            GateKind = testRun.GateKind,
            Ttl = TestRunDocument.SandboxRetentionSeconds,
        };

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

        var doc = new DesignTurnDocument
        {
            Id = turn.Id,
            WorkflowId = turn.WorkflowId,
            DraftId = turn.DraftId,
            TurnNumber = turn.TurnNumber,
            DesignerId = turn.DesignerId,
            CreatedAtUtc = turn.CreatedAtUtc,
            DesignerMessage = turn.DesignerMessage,
            DraftRevisionAtTurn = turn.DraftRevisionAtTurn,
            AgentProposalJson = turn.AgentProposalJson,
            ProposalReasoningJson = turn.ProposalReasoningJson,
            ProposalChanges = turn.ProposalChanges,
            ProposalBlockReason = turn.ProposalBlockReason,
            MergeOutcome = turn.MergeOutcome,
            ConflictSummary = turn.ConflictSummary
        };

        var response = await _container.UpsertItemAsync(doc, new PartitionKey(turn.WorkflowId), cancellationToken: ct);
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
                "SELECT c.workflowId, c.id, c.state, c.definitionVersion, c.approvedUtc, c.approvedBy, c.definition.name AS definitionName " +
                "FROM c WHERE c.state IN ('draft', 'published', 'superseded', 'retired')"));

        var rows = new List<CatalogueRow>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            rows.AddRange(page);
        }

        // Group rows by workflowId and derive an effective status per workflow.
        var byWorkflow = rows.GroupBy(r => r.WorkflowId, StringComparer.Ordinal);
        var summaries = new List<WorkflowCatalogueSummary>();

        foreach (var group in byWorkflow)
        {
            var wfId = group.Key;
            var versions = group
                .Where(r => r.State is "published" or "superseded" or "retired")
                .OrderByDescending(r => r.DefinitionVersion ?? 0)
                .ToList();

            string effectiveStatus;
            DateTime? lastPublishedAt = null;
            string? lastPublishedBy = null;

            if (versions.Count > 0)
            {
                var latestPublished = versions.FirstOrDefault(v => v.State == "published");
                effectiveStatus = latestPublished is not null ? "published" : "retired";
                if (latestPublished is not null)
                {
                    lastPublishedAt = latestPublished.ApprovedUtc;
                    lastPublishedBy = latestPublished.ApprovedBy;
                }
            }
            else
            {
                effectiveStatus = "draft";
            }

            var resolvedName = group
                .Select(r => r.DefinitionName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? wfId;

            summaries.Add(new WorkflowCatalogueSummary(
                WorkflowId: wfId,
                Name: resolvedName,
                EngagementType: null,
                Status: effectiveStatus,
                LastPublishedAt: lastPublishedAt,
                LastPublishedBy: lastPublishedBy));
        }

        // In-memory filters (Phase 1 — catalogue sizes are small).
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

    /// <summary>Projection record for the catalogue cross-partition query.</summary>
    private sealed record CatalogueRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("workflowId")]
        public string WorkflowId { get; init; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; init; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("state")]
        public string State { get; init; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("definitionVersion")]
        public int? DefinitionVersion { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("approvedUtc")]
        public DateTime? ApprovedUtc { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("approvedBy")]
        public string? ApprovedBy { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("definitionName")]
        public string? DefinitionName { get; init; }
    }
}
