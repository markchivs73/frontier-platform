using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Hitl;
using Frontier.Platform.Resilience;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The deterministic step logic behind <see cref="GraphOrchestrator"/> (doc 00 §4, S2.2):
/// an initial topological walk over every <see cref="AgentTaskNode"/>, followed by one
/// bounded wait for a <c>ArtifactUpdated</c> external event that, if received, triggers a
/// cascade evaluation and a topological re-walk of the affected sections. Extracted from
/// <see cref="GraphOrchestrator"/> so the orchestrator class itself stays a thin
/// <c>[DurableTask]</c> entry point (engineering-standards: no private methods, small
/// internal helpers).
/// </summary>
internal static class GraphOrchestratorSteps
{
    /// <summary>External event name a running execution listens for once its initial walk completes (S2.2 PoC; doc 03 §9 cascade re-trigger).</summary>
    internal const string ArtifactUpdatedEventName = "ArtifactUpdated";

    /// <summary>How long the orchestration waits for <see cref="ArtifactUpdatedEventName"/> before completing without a cascade (S2.2 PoC placeholder).</summary>
    internal static readonly TimeSpan ArtifactUpdateWaitWindow = TimeSpan.FromMinutes(5);

    /// <summary>The Resilience profile (doc 10 §4) for an <see cref="AgentTaskNode"/> that specifies no <see cref="RetryPolicySpec"/>.</summary>
    internal const string LlmDefaultProfile = "llm-default";

    /// <summary>The Resilience profile (doc 10 §4) for an <see cref="McpToolNode"/> read call with no <see cref="RetryPolicySpec"/> (S13.7c).</summary>
    internal const string McpReadProfile = "mcp-read";

    /// <summary>The Resilience profile (doc 10 §4) for an <see cref="McpToolNode"/> write call with no <see cref="RetryPolicySpec"/> (S13.7c).</summary>
    internal const string McpWriteProfile = "mcp-write";

    /// <summary>The Resilience profile (doc 10 §4, ADR-S3) for <see cref="WorkflowActivityNames.SnapshotStateActivity"/> calls.</summary>
    internal const string SnapshotPersistenceProfile = "snapshot-persistence";

    /// <summary>Prefix for a <see cref="HumanGateNode"/>'s decision external event name (doc 06 §3).</summary>
    internal const string GateEventNamePrefix = "Gate:";

    /// <summary>
    /// Runs every <see cref="AgentTaskNode"/>/<see cref="HumanGateNode"/> once via the
    /// ADR-5 ready-set scheduler (S13.7i): all ready non-gate nodes are scheduled
    /// concurrently (the DTF fan-out pattern — the orchestrator's single-threaded event
    /// loop keeps state mutation race-free; history order keeps replay deterministic),
    /// gates run only at full quiescence (Decision 2), and a permanent failure lets
    /// in-flight siblings finish before the walk pauses attributed (Decision 4).
    /// </summary>
    internal static async Task<GraphExecutionState> RunInitialWalkAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, IRollbackPlanner rollbackPlanner, IResiliencePolicyProvider policyProvider, IMcpWriteClassifier mcpWriteClassifier)
    {
        EnsureSupported(input.Definition);

        var state = new GraphExecutionState { StartedAtUtc = context.CurrentUtcDateTime };
        var walk = GraphWalk.Create(input.Definition);

        while (walk.HasWork)
        {
            if (walk.FirstFailure is null && await TryScheduleAsync(context, input, walk, state, rollbackPlanner, policyProvider, mcpWriteClassifier))
            {
                continue;
            }

            if (walk.Running.Count == 0)
            {
                break;
            }

            await Task.WhenAny(walk.Running.Values);
            ObserveFinished(walk);
        }

        await ThrowIfFailedAsync(context, input, state, walk, policyProvider);
        walk.ThrowIfIncomplete();
        return state;
    }

    /// <summary>
    /// Schedules the ready frontier: every ready non-gate node starts concurrently; a
    /// gate runs inline (and returns <see langword="true"/> so the loop re-enters
    /// scheduling) only once the walk has quiesced (ADR-5 Decisions 1–2).
    /// </summary>
    internal static async Task<bool> TryScheduleAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphWalk walk, GraphExecutionState state, IRollbackPlanner rollbackPlanner, IResiliencePolicyProvider policyProvider, IMcpWriteClassifier mcpWriteClassifier)
    {
        while (walk.TakeReadyDecision() is { } decisionId)
        {
            await RunDecisionAsync(context, input, (DecisionNode)walk.NodesById[decisionId], walk, state, policyProvider);
        }

        foreach (var nodeId in walk.TakeReadyAgentNodes())
        {
            walk.Running[nodeId] = walk.NodesById[nodeId] switch
            {
                McpToolNode toolNode => RunMcpToolNodeAsync(context, input, toolNode, state, policyProvider, mcpWriteClassifier),
                var node => RunNodeAsync(context, input, (AgentTaskNode)node, state, policyProvider),
            };
        }

        var gateId = walk.TakeReadyGateWhenQuiesced();
        if (gateId is null)
        {
            return false;
        }

        await RunGateAsync(context, input, (HumanGateNode)walk.NodesById[gateId], state, rollbackPlanner, policyProvider);
        walk.Complete(gateId);
        return true;
    }

    /// <summary>
    /// Runs one <see cref="McpToolNode"/> via <see cref="WorkflowActivityNames.InvokeMcpToolActivity"/>
    /// (S13.7c): a deterministic tool call — no agent, no model. The retry profile defaults
    /// to doc 10 §4's <c>mcp-write</c>/<c>mcp-read</c> by the tool's write classification
    /// (a node <see cref="RetryPolicySpec"/> overrides); a write's idempotency key is the
    /// step's correlation id, so activity retries provably reuse it. The result records,
    /// checkpoints, and feeds Data edges/predicates exactly like an agent step.
    /// </summary>
    internal static async Task RunMcpToolNodeAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, McpToolNode node, GraphExecutionState state, IResiliencePolicyProvider policyProvider, IMcpWriteClassifier mcpWriteClassifier)
    {
        var occurrence = state.NodeOccurrences.GetValueOrDefault(node.NodeId);
        state.NodeOccurrences[node.NodeId] = occurrence + 1;
        var correlationId = $"{context.InstanceId}::{node.NodeId}::{occurrence}";
        var isWrite = mcpWriteClassifier.IsWrite(McpToolRef.Parse(node.ToolRef));

        var activityInput = new McpToolActivityInput
        {
            NodeId = node.NodeId,
            ArtifactKey = node.ArtifactKey,
            ToolRef = node.ToolRef,
            TimeoutSeconds = node.TimeoutSeconds,
            IdempotencyKey = isWrite ? correlationId : null,
            CorrelationId = correlationId,
            ExecutionId = context.InstanceId,
            EngagementId = input.EngagementId,
            InputPayload = ResolveUpstreamPayloadForNode(input.Definition, node.NodeId, state),
        };

        var taskOptions = policyProvider.GetTaskOptions(node.Retry?.ProfileName ?? (isWrite ? McpWriteProfile : McpReadProfile));
        var result = await context.CallActivityAsync<McpToolActivityResult>(WorkflowActivityNames.InvokeMcpToolActivity, activityInput, taskOptions);

        state.NodeOutputPayloads[node.NodeId] = result.OutputPayload;
        state.CompletedSteps.Add(new StepCompletion
        {
            NodeId = node.NodeId,
            NodeType = NodeType.McpTool,
            ArtifactKey = node.ArtifactKey,
            CorrelationId = correlationId,
            OutputContractType = string.Empty,
            OutputHash = result.OutputHash,
            RetryCount = 0,
            CompletedAtUtc = context.CurrentUtcDateTime,
            HostBuild = result.HostBuild,
        });

        if (node.ArtifactKey is not null)
        {
            state.ArtifactStatuses[node.ArtifactKey] = ArtifactStatus.Draft;
            await WriteArtifactVersionAsync(context, input, state, node.ArtifactKey, result.OutputPayload, result.OutputHash);
        }

        await WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, node.NodeId, policyProvider);
    }

    /// <summary>Resolves any node's single upstream Data-edge payload by node id (the S13.7c generalisation of <see cref="ResolveUpstreamPayload"/>; one inbound Data edge per node, ADR-5 D3).</summary>
    internal static string? ResolveUpstreamPayloadForNode(WorkflowDefinition definition, string nodeId, GraphExecutionState state)
    {
        var predecessor = definition.Edges.FirstOrDefault(edge => edge.Kind == EdgeKind.Data && edge.ToNodeId == nodeId);
        return predecessor is null ? null : state.NodeOutputPayloads[predecessor.FromNodeId];
    }

    /// <summary>
    /// Runs one <see cref="DecisionNode"/> purely in the body (ADR-CD4/ADR-5 Decision 6,
    /// S13.7j): evaluates the branch predicates over recorded section outputs, records
    /// the routing fact as a <see cref="StepCompletion"/> (no activity — decisions have
    /// no output contract), kills the unselected subtrees, syncs any resulting skips
    /// into <see cref="GraphExecutionState.SkippedNodeIds"/>, and checkpoints.
    /// </summary>
    internal static async Task RunDecisionAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, DecisionNode decision, GraphWalk walk, GraphExecutionState state, IResiliencePolicyProvider policyProvider)
    {
        var occurrence = state.NodeOccurrences.GetValueOrDefault(decision.NodeId);
        state.NodeOccurrences[decision.NodeId] = occurrence + 1;
        var selectedTargetId = PredicateEvaluator.SelectBranch(decision, input.Definition, state);

        state.CompletedSteps.Add(new StepCompletion
        {
            NodeId = decision.NodeId,
            NodeType = NodeType.Decision,
            ArtifactKey = null,
            CorrelationId = $"{context.InstanceId}::{decision.NodeId}::{occurrence}",
            OutputContractType = string.Empty,
            OutputHash = string.Empty,
            RetryCount = 0,
            CompletedAtUtc = context.CurrentUtcDateTime,
            SelectedBranchNodeId = selectedTargetId,
        });

        walk.CompleteDecision(decision.NodeId, selectedTargetId);
        SyncSkips(walk, state);
        await WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, decision.NodeId, policyProvider);
    }

    /// <summary>Copies newly-skipped node ids from the walk into the snapshot-visible state, in skip order.</summary>
    internal static void SyncSkips(GraphWalk walk, GraphExecutionState state)
    {
        for (var next = state.SkippedNodeIds.Count; next < walk.SkippedNodeIds.Count; next++)
        {
            state.SkippedNodeIds.Add(walk.SkippedNodeIds[next]);
        }
    }

    /// <summary>Drains finished node tasks: completions release successors; the first fault is recorded for attributed pausing (ADR-5 Decision 4), later sibling faults are absorbed.</summary>
    internal static void ObserveFinished(GraphWalk walk)
    {
        foreach (var (nodeId, task) in walk.DrainFinished())
        {
            if (task.IsFaulted)
            {
                walk.Fail(nodeId, UnwrapTaskException(task.Exception!));
            }
            else
            {
                walk.Complete(nodeId);
            }
        }
    }

    /// <summary>
    /// After a failure drain (ADR-5 Decision 4): writes the final
    /// <see cref="ExecutionStatus.PausedOnFailure"/> checkpoint carrying the actually
    /// failing node id and its doc 10 §3 classification — the real-execution half of the
    /// C-35 attribution fix — then rethrows the original failure so DTF records it.
    /// </summary>
    internal static async Task ThrowIfFailedAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, GraphWalk walk, IResiliencePolicyProvider policyProvider)
    {
        if (walk.FirstFailure is null)
        {
            return;
        }

        await WriteSnapshotAsync(context, input, state, ExecutionStatus.PausedOnFailure, walk.FailedNodeId, policyProvider, failureClassification: ClassifyFailure(walk.FirstFailure));
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(walk.FirstFailure).Throw();
    }

    /// <summary>Unwraps the single inner exception a faulted node task carries.</summary>
    internal static Exception UnwrapTaskException(AggregateException aggregate) => aggregate.InnerException ?? aggregate;

    /// <summary>
    /// Doc 10 §3 reason code for a failed node task, mirroring the Host's
    /// <c>OrchestrationFactory.ClassifyFailure</c> vocabulary: activity failures cross
    /// the DTF boundary as <see cref="TaskFailedException"/>, so the chain of
    /// <see cref="TaskFailureDetails"/> is walked; in-body exceptions match directly.
    /// </summary>
    internal static string ClassifyFailure(Exception exception) => exception switch
    {
        ContractViolationException => "contract_violation",
        BudgetExceededException => "guardrail",
        TaskFailedException failed => ClassifyFailureDetails(failed.FailureDetails),
        _ => "unclassified",
    };

    /// <summary>Walks a <see cref="TaskFailureDetails"/> chain for the doc 10 §3 reason code (each level checks only itself, so recurse).</summary>
    internal static string ClassifyFailureDetails(TaskFailureDetails? details) => details switch
    {
        null => "unclassified",
        { } d when d.IsCausedBy<ContractViolationException>() => "contract_violation",
        { } d when d.IsCausedBy<BudgetExceededException>() => "guardrail",
        _ => ClassifyFailureDetails(details.InnerFailure),
    };

    /// <summary>Waits for <see cref="ArtifactUpdatedEventName"/>; on receipt, evaluates the cascade and re-walks the downstream sections it returns.</summary>
    internal static async Task RunCascadeWalkAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, IResiliencePolicyProvider policyProvider, IMcpWriteClassifier mcpWriteClassifier)
    {
        var changedArtifact = await TryWaitForArtifactUpdateAsync(context);
        if (changedArtifact is null)
        {
            return;
        }

        var cascade = await EvaluateCascadeAsync(context, input.Definition, state, changedArtifact);
        await RegenerateDownstreamAsync(context, input, state, cascade.DownstreamArtifacts, policyProvider);
    }

    /// <summary>Writes the final <see cref="ExecutionStatus.Completed"/> checkpoint once the initial and cascade walks have finished (doc 02 §5).</summary>
    internal static Task WriteFinalSnapshotAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, IResiliencePolicyProvider policyProvider) =>
        WriteSnapshotAsync(context, input, state, ExecutionStatus.Completed, currentNodeId: null, policyProvider);

    /// <summary>
    /// Calls <see cref="WorkflowActivityNames.ConsolidateAuditActivity"/> after the final
    /// snapshot write (doc 05 §8): the orchestrator's last activity on full-graph
    /// completion, consolidating this execution's evidence into a signed, chained
    /// <c>audit-records</c> entry. Reuses the <see cref="SnapshotPersistenceProfile"/> retry
    /// profile (S4.4) — the same Cosmos-write characteristics as the snapshot writer it
    /// follows. <paramref name="startedAtUtc"/> is <c>context.CurrentUtcDateTime</c> captured
    /// once at orchestration start (deterministic, ADR-2).
    /// </summary>
    internal static Task ConsolidateAuditAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, DateTime startedAtUtc, IResiliencePolicyProvider policyProvider)
    {
        var request = new ConsolidateAuditInput
        {
            ExecutionId = context.InstanceId,
            EngagementId = input.EngagementId,
            WorkflowId = input.Definition.WorkflowId,
            DefinitionHash = input.Definition.DefinitionHash,
            StartedAtUtc = startedAtUtc,
            RunId = input.RunId,
        };

        var taskOptions = policyProvider.GetTaskOptions(SnapshotPersistenceProfile);
        return context.CallActivityAsync<SignedAuditRecord>(WorkflowActivityNames.ConsolidateAuditActivity, request, taskOptions);
    }

    /// <summary>Throws if <paramref name="definition"/> uses anything beyond the S2.2 PoC's supported shape: <see cref="ExecutionMode.OneShot"/>, all <see cref="AgentTaskNode"/>.</summary>
    internal static void EnsureSupported(WorkflowDefinition definition)
    {
        if (definition.Mode != ExecutionMode.OneShot)
        {
            throw new ContractViolationException(nameof(WorkflowDefinition), [$"GraphOrchestrator (S2.2 PoC) supports only '{ExecutionMode.OneShot.Name}' definitions; got '{definition.Mode.Name}'."]);
        }

        // S13.7h: the supported set lives in OrchestratorCapabilities so the designer's schema
        // advertises exactly what this check enforces — they cannot drift apart.
        var unsupported = definition.Nodes.Where(node => !OrchestratorCapabilities.Supports(node.NodeType)).Select(node => node.NodeId).ToList();
        if (unsupported.Count > 0)
        {
            var supported = string.Join("'/'", OrchestratorCapabilities.SupportedNodeTypes.Select(t => t.Name));
            throw new ContractViolationException(nameof(WorkflowDefinition), [$"GraphOrchestrator (S4.6 PoC) supports only '{supported}' nodes; unsupported node(s): {string.Join(", ", unsupported)}."]);
        }
    }

    /// <summary>
    /// Runs one <see cref="AgentTaskNode"/> via <see cref="AgentTaskActivity"/>, recording
    /// its <see cref="StepCompletion"/>, advancing its section to <see cref="ArtifactStatus.Draft"/>
    /// and appending its new <c>artifact-state</c> version via <see cref="WriteArtifactVersionAsync"/>,
    /// then writing the resulting checkpoint via <see cref="WriteSnapshotAsync"/> (doc 02 §5:
    /// one snapshot write per node completion).
    /// </summary>
    internal static async Task RunNodeAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, AgentTaskNode node, GraphExecutionState state, IResiliencePolicyProvider policyProvider, string? revisionNote = null)
    {
        var occurrence = state.NodeOccurrences.GetValueOrDefault(node.NodeId);
        state.NodeOccurrences[node.NodeId] = occurrence + 1;
        var correlationId = $"{context.InstanceId}::{node.NodeId}::{occurrence}";
        var taskOptions = policyProvider.GetTaskOptions(node.Retry?.ProfileName ?? LlmDefaultProfile);
        var activityInput = BuildActivityInput(input, node, correlationId, context.InstanceId, state, revisionNote);
        var result = await context.CallActivityAsync<AgentTaskActivityResult>(WorkflowActivityNames.AgentTaskActivity, activityInput, taskOptions);

        state.NodeOutputPayloads[node.NodeId] = result.OutputPayload;
        state.CompletedSteps.Add(BuildStepCompletion(context, node, correlationId, result));

        if (node.ArtifactKey is not null)
        {
            state.ArtifactStatuses[node.ArtifactKey] = ArtifactStatus.Draft;
            await WriteArtifactVersionAsync(context, input, state, node.ArtifactKey, result.OutputPayload, result.OutputHash);
        }

        await WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, node.NodeId, policyProvider);
    }

    /// <summary>Projects an <see cref="AgentTaskNode"/> and its minted <paramref name="correlationId"/> into <see cref="AgentTaskActivity"/>'s input shape.</summary>
    internal static AgentTaskActivityInput BuildActivityInput(GraphOrchestratorInput input, AgentTaskNode node, string correlationId, string executionId, GraphExecutionState state, string? revisionNote = null) => new()
    {
        NodeId = node.NodeId,
        ArtifactKey = node.ArtifactKey,
        Role = node.Role,
        InstructionsRef = node.InstructionsRef,
        InputContractType = node.InputContractType,
        OutputContractType = node.OutputContractType,
        // S9.28: a published definition runs across many engagements (ADR-2), so the chat
        // designer cannot author a real engagement id at design time into
        // AgentTaskNode.ContextRequest.EngagementId — the orchestrator substitutes the real
        // running engagement here, the same way it already does for the sibling EngagementId
        // field below. Never caught before S9.28 because every gate/demo fixture runs the
        // single seeded ENGAGEMENT-12345 that happens to match whatever placeholder a node's
        // authored ContextRequest carried.
        ContextRequest = node.ContextRequest with { EngagementId = input.EngagementId },
        CorrelationId = correlationId,
        EngagementId = input.EngagementId,
        ExecutionId = executionId,
        UpstreamPayload = ResolveUpstreamPayload(input.Definition, node, state),
        RevisionNote = revisionNote,
        ToolRefs = node.ToolRefs,
    };

    /// <summary>
    /// Resolves <paramref name="node"/>'s upstream Data-edge payload from
    /// <see cref="GraphExecutionState.NodeOutputPayloads"/>, or <see langword="null"/> if
    /// <paramref name="node"/> has no Data-edge predecessor (e.g. <c>gen-scope</c>, S4.1).
    /// </summary>
    internal static string? ResolveUpstreamPayload(WorkflowDefinition definition, AgentTaskNode node, GraphExecutionState state)
    {
        var predecessor = definition.Edges.FirstOrDefault(edge => edge.Kind == EdgeKind.Data && edge.ToNodeId == node.NodeId);
        return predecessor is null ? null : state.NodeOutputPayloads[predecessor.FromNodeId];
    }

    /// <summary>Builds the <see cref="StepCompletion"/> record for a finished <see cref="AgentTaskNode"/> invocation.</summary>
    internal static StepCompletion BuildStepCompletion(TaskOrchestrationContext context, AgentTaskNode node, string correlationId, AgentTaskActivityResult result) => new()
    {
        NodeId = node.NodeId,
        NodeType = node.NodeType,
        ArtifactKey = node.ArtifactKey,
        CorrelationId = correlationId,
        OutputContractType = result.OutputContractType,
        OutputHash = result.OutputHash,
        RetryCount = 0,
        CompletedAtUtc = context.CurrentUtcDateTime,
        ResolvedModel = result.ResolvedModel,
        HostBuild = result.HostBuild,
    };

    /// <summary>
    /// Waits up to <see cref="ArtifactUpdateWaitWindow"/> for <see cref="ArtifactUpdatedEventName"/>
    /// via the SDK's built-in <c>WaitForExternalEvent(name, timeout)</c> overload, which manages
    /// its own internal <c>CreateTimer</c>/cancellation pairing (a hand-rolled
    /// <c>Task.WhenAny</c> + <c>CreateTimer</c> + <c>CancellationTokenSource.Cancel</c> races a
    /// <c>TimerFired</c> history replay against the already-cancelled timer task and throws
    /// <see cref="InvalidOperationException"/>). Returns the changed section key, or
    /// <c>null</c> if the window elapses first.
    /// </summary>
    internal static async Task<string?> TryWaitForArtifactUpdateAsync(TaskOrchestrationContext context)
    {
        try
        {
            return await context.WaitForExternalEvent<string>(ArtifactUpdatedEventName, ArtifactUpdateWaitWindow);
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Calls <see cref="WorkflowActivityNames.EvaluateCascadeActivity"/> for <paramref name="changedArtifact"/> against the execution's current section statuses.</summary>
    internal static Task<CascadeActivityResponse> EvaluateCascadeAsync(TaskOrchestrationContext context, WorkflowDefinition definition, GraphExecutionState state, string changedArtifact)
    {
        var request = new CascadeActivityRequest
        {
            Definition = definition,
            Request = new CascadeEvalRequestPayload
            {
                ExecutionId = context.InstanceId,
                ChangedArtifact = changedArtifact,
                CurrentArtifactStatuses = state.ArtifactStatuses,
            },
        };

        return context.CallActivityAsync<CascadeActivityResponse>(WorkflowActivityNames.EvaluateCascadeActivity, request);
    }

    /// <summary>
    /// Re-runs every <see cref="AgentTaskNode"/> whose section appears in
    /// <paramref name="downstreamArtifacts"/>, in the order given (already topological per
    /// doc 03 §3). <paramref name="revisionNote"/> threads a gate rejection's note (doc 06
    /// §13, S4.6) onto each regenerated node's <see cref="AgentTaskActivityInput.RevisionNote"/>.
    /// </summary>
    internal static async Task RegenerateDownstreamAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, IReadOnlyList<string> downstreamArtifacts, IResiliencePolicyProvider policyProvider, string? revisionNote = null)
    {
        var nodesByArtifact = input.Definition.Nodes
            .OfType<AgentTaskNode>()
            .Where(node => node.ArtifactKey is not null)
            .ToDictionary(node => node.ArtifactKey!, StringComparer.Ordinal);

        foreach (var section in downstreamArtifacts.Where(nodesByArtifact.ContainsKey))
        {
            await RunNodeAsync(context, input, nodesByArtifact[section], state, policyProvider, revisionNote);
        }
    }

    /// <summary>
    /// Calls <see cref="WorkflowActivityNames.ArtifactStateActivity"/> to append
    /// <paramref name="sectionKey"/>'s next version (doc 02 §2-3), advancing
    /// <see cref="GraphExecutionState.ArtifactVersions"/> from 0 so the first write for a
    /// section is version 1, never derived from wall-clock time. Records the written
    /// version's ref in <see cref="GraphExecutionState.SectionRefs"/> (S4.6, doc 06 §9).
    /// </summary>
    internal static async Task WriteArtifactVersionAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, string sectionKey, string content, string contentHash)
    {
        var version = (state.ArtifactVersions.GetValueOrDefault(sectionKey)) + 1;
        state.ArtifactVersions[sectionKey] = version;

        var request = new ArtifactStateActivityRequest
        {
            ExecutionId = context.InstanceId,
            EngagementId = input.EngagementId,
            ArtifactKey = sectionKey,
            Version = version,
            Content = content,
            ContentHash = contentHash,
            UpdatedAtUtc = context.CurrentUtcDateTime,
        };

        var response = await context.CallActivityAsync<ArtifactStateActivityResponse>(WorkflowActivityNames.ArtifactStateActivity, request);
        state.SectionRefs[sectionKey] = response.SectionRef;
    }

    /// <summary>
    /// Calls <see cref="WorkflowActivityNames.SnapshotStateActivity"/> with the
    /// <see cref="ExecutionSnapshot"/> built from <paramref name="state"/> at its current
    /// <see cref="GraphExecutionState.Sequence"/>, then advances the counter (doc 02 §5:
    /// sequence is the orchestrator's own checkpoint counter, never wall-clock).
    /// </summary>
    internal static async Task WriteSnapshotAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, ExecutionStatus status, string? currentNodeId, IResiliencePolicyProvider policyProvider, string? pausedAtGateId = null, string? failureClassification = null)
    {
        var snapshot = BuildSnapshot(context, input, state, status, currentNodeId, pausedAtGateId, failureClassification);
        var taskOptions = policyProvider.GetTaskOptions(SnapshotPersistenceProfile);
        await context.CallActivityAsync<SnapshotActivityResponse>(WorkflowActivityNames.SnapshotStateActivity, snapshot, taskOptions);
        state.Sequence++;
    }

    /// <summary>Projects <paramref name="state"/> into the <see cref="ExecutionSnapshot"/> for the current checkpoint (doc 02 §2, §5).</summary>
    internal static ExecutionSnapshot BuildSnapshot(TaskOrchestrationContext context, GraphOrchestratorInput input, GraphExecutionState state, ExecutionStatus status, string? currentNodeId, string? pausedAtGateId = null, string? failureClassification = null) => new()
    {
        ExecutionId = context.InstanceId,
        EngagementId = input.EngagementId,
        WorkflowId = input.Definition.WorkflowId,
        DefinitionVersion = input.Definition.DefinitionVersion,
        Sequence = state.Sequence,
        Status = status,
        CurrentNodeId = currentNodeId,
        PausedAtGateId = pausedAtGateId,
        FailureClassification = failureClassification,
        SkippedNodeIds = state.SkippedNodeIds.Count > 0 ? [.. state.SkippedNodeIds] : null,
        Artifacts = new Dictionary<string, ArtifactStatus>(state.ArtifactStatuses),
        CompletedSteps = [.. state.CompletedSteps],
        Decisions = [.. state.Decisions],
        ApprovedSnapshotRefs = new Dictionary<string, string>(state.ApprovedSnapshotRefs),
        CheckpointedAtUtc = context.CurrentUtcDateTime,
        InitiatedBy = input.InitiatedBy,
        RunId = input.RunId,
        StartedAtUtc = state.StartedAtUtc,
    };

    /// <summary>
    /// Runs a <see cref="HumanGateNode"/> to completion (doc 06 §3-§7, §13, S4.6): opens
    /// an approval request, checkpoints <see cref="ExecutionStatus.PausedAtGate"/>, waits
    /// for the decision, then either records approvals or runs the rejection's
    /// rollback/regeneration cascade. If <see cref="HumanGateNode.ReapproveOnCascade"/> is
    /// <see langword="true"/> (the default), a rejection loops back to re-open the same
    /// gate for the regenerated sections; otherwise the gate exits without re-approval
    /// (a PoC interpretation — doc 06 does not fully specify this case).
    /// </summary>
    internal static async Task RunGateAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, HumanGateNode gate, GraphExecutionState state, IRollbackPlanner rollbackPlanner, IResiliencePolicyProvider policyProvider)
    {
        while (true)
        {
            var approvalRequest = await OpenGateAsync(context, input, gate, state);
            await WriteSnapshotAsync(context, input, state, ExecutionStatus.PausedAtGate, gate.NodeId, policyProvider, gate.NodeId);

            var decision = await DecideAsync(context, gate, approvalRequest);
            state.Decisions.Add(decision);

            if (decision.Kind == DecisionKind.Approve)
            {
                RecordApprovals(state);
                await WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, gate.NodeId, policyProvider);
                return;
            }

            await HandleRejectionAsync(context, input, gate, decision, state, rollbackPlanner, policyProvider);

            if (!gate.ReapproveOnCascade)
            {
                await WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, gate.NodeId, policyProvider);
                return;
            }
        }
    }

    /// <summary>
    /// Calls <see cref="WorkflowActivityNames.RequestApprovalActivity"/> to open
    /// <paramref name="gate"/>'s approval request at its current
    /// <see cref="GraphExecutionState.GateOccurrences"/> count, then increments it for any
    /// re-entry after rollback (doc 06 §4, §9, §13).
    /// </summary>
    internal static Task<ApprovalRequest> OpenGateAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, HumanGateNode gate, GraphExecutionState state)
    {
        var occurrence = state.GateOccurrences.GetValueOrDefault(gate.NodeId);
        state.GateOccurrences[gate.NodeId] = occurrence + 1;

        var request = new GateOpenRequest
        {
            ExecutionId = context.InstanceId,
            EngagementId = input.EngagementId,
            GateId = gate.NodeId,
            GateKind = gate.GateKind,
            ApproverRoles = gate.ApproverRoles,
            SectionRefs = new Dictionary<string, string>(state.SectionRefs),
            Occurrence = occurrence,
            TimeoutMinutes = gate.TimeoutMinutes,
            RequestedAtUtc = context.CurrentUtcDateTime,
        };

        return context.CallActivityAsync<ApprovalRequest>(WorkflowActivityNames.RequestApprovalActivity, request);
    }

    /// <summary>
    /// Waits for <paramref name="gate"/>'s decision (doc 06 §3, §7), looping past any
    /// <see cref="DecisionKind.Escalate"/> the approver themselves submits — escalation
    /// re-routes and reminds, it never auto-decides, so the gate keeps waiting on the
    /// same decision task.
    /// </summary>
    internal static async Task<HitlDecision> DecideAsync(TaskOrchestrationContext context, HumanGateNode gate, ApprovalRequest approvalRequest)
    {
        var eventName = GateEventName(gate.NodeId);
        var decision = await WaitForGateDecisionAsync(context, gate, eventName, approvalRequest);

        while (decision.Kind == DecisionKind.Escalate)
        {
            await EscalateAsync(context, approvalRequest);
            decision = await context.WaitForExternalEvent<HitlDecision>(eventName);
        }

        return decision;
    }

    /// <summary>
    /// Waits for <paramref name="eventName"/> up to <see cref="HumanGateNode.TimeoutMinutes"/>
    /// (<c>0</c> = no escalation, doc 06 §3); on timeout, calls
    /// <see cref="WorkflowActivityNames.EscalateApprovalActivity"/> (doc 06 §7) and then
    /// waits indefinitely for the same decision task.
    /// </summary>
    internal static async Task<HitlDecision> WaitForGateDecisionAsync(TaskOrchestrationContext context, HumanGateNode gate, string eventName, ApprovalRequest approvalRequest)
    {
        if (gate.TimeoutMinutes <= 0)
        {
            return await context.WaitForExternalEvent<HitlDecision>(eventName);
        }

        try
        {
            return await context.WaitForExternalEvent<HitlDecision>(eventName, TimeSpan.FromMinutes(gate.TimeoutMinutes));
        }
        catch (TaskCanceledException)
        {
            await EscalateAsync(context, approvalRequest);
            return await context.WaitForExternalEvent<HitlDecision>(eventName);
        }
    }

    /// <summary>Calls <see cref="WorkflowActivityNames.EscalateApprovalActivity"/> to mark <paramref name="approvalRequest"/> escalated (doc 06 §7).</summary>
    internal static Task<ApprovalRequest> EscalateAsync(TaskOrchestrationContext context, ApprovalRequest approvalRequest) =>
        context.CallActivityAsync<ApprovalRequest>(WorkflowActivityNames.EscalateApprovalActivity, approvalRequest);

    /// <summary>Builds a <see cref="HumanGateNode"/>'s decision external event name (doc 06 §3): <c>Gate:{gateId}</c>.</summary>
    internal static string GateEventName(string gateId) => $"{GateEventNamePrefix}{gateId}";

    /// <summary>On <see cref="DecisionKind.Approve"/> (doc 06 §3): every <see cref="ArtifactStatus.Draft"/> section becomes <see cref="ArtifactStatus.Approved"/> and its current ref is captured into <see cref="GraphExecutionState.ApprovedSnapshotRefs"/>.</summary>
    internal static void RecordApprovals(GraphExecutionState state)
    {
        foreach (var section in state.ArtifactStatuses.Keys.Where(section => state.ArtifactStatuses[section] == ArtifactStatus.Draft).ToList())
        {
            state.ArtifactStatuses[section] = ArtifactStatus.Approved;
            state.ApprovedSnapshotRefs[section] = state.SectionRefs[section];
        }
    }

    /// <summary>
    /// On <see cref="DecisionKind.Reject"/> (doc 06 §6, §13): plans the restore/regenerate
    /// split via <see cref="IRollbackPlanner"/>, repoints the restore set's section
    /// pointers, marks the invalid set <see cref="ArtifactStatus.Regenerating"/>, and
    /// regenerates it with the rejection's note threaded onto each agent's
    /// <see cref="AgentTaskActivityInput.RevisionNote"/>.
    /// </summary>
    internal static async Task HandleRejectionAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, HumanGateNode gate, HitlDecision decision, GraphExecutionState state, IRollbackPlanner rollbackPlanner, IResiliencePolicyProvider policyProvider)
    {
        var rollbackTargetArtifact = ResolveRollbackTargetArtifact(input.Definition, gate);
        var cascade = await EvaluateCascadeAsync(context, input.Definition, state, rollbackTargetArtifact);
        var plan = rollbackPlanner.Plan(rollbackTargetArtifact, cascade.DownstreamArtifacts, state.ApprovedSnapshotRefs);

        await RestoreArtifactsAsync(context, input, plan.RestoreSet, state);
        MarkInvalidSetRegenerating(state, plan.InvalidSet);
        await RegenerateDownstreamAsync(context, input, state, plan.InvalidSet, policyProvider, decision.Notes);
    }

    /// <summary>Resolves <paramref name="gate"/>'s <see cref="HumanGateNode.RollbackToNodeId"/> to its <see cref="WorkflowNode.ArtifactKey"/> (doc 06 §6); reject-in-place (<see langword="null"/>) is unsupported by the PoC orchestrator.</summary>
    internal static string ResolveRollbackTargetArtifact(WorkflowDefinition definition, HumanGateNode gate)
    {
        var targetNode = gate.RollbackToNodeId is null
            ? null
            : definition.Nodes.FirstOrDefault(node => node.NodeId == gate.RollbackToNodeId);

        return targetNode?.ArtifactKey
            ?? throw new ContractViolationException(nameof(HumanGateNode), [$"Gate '{gate.NodeId}' rejected with no resolvable RollbackToNodeId/ArtifactKey; reject-in-place is unsupported by the PoC orchestrator."]);
    }

    /// <summary>Calls <see cref="WorkflowActivityNames.RestoreArtifactsActivity"/> for every section in <paramref name="restoreSet"/>, updating <see cref="GraphExecutionState.SectionRefs"/> from the response (doc 06 §6).</summary>
    internal static async Task RestoreArtifactsAsync(TaskOrchestrationContext context, GraphOrchestratorInput input, IReadOnlyDictionary<string, string> restoreSet, GraphExecutionState state)
    {
        foreach (var (section, restoreRef) in restoreSet)
        {
            var request = new ArtifactRestoreActivityRequest
            {
                EngagementId = input.EngagementId,
                RestoreRef = restoreRef,
                RestoredAtUtc = context.CurrentUtcDateTime,
            };

            var response = await context.CallActivityAsync<ArtifactStateActivityResponse>(WorkflowActivityNames.RestoreArtifactsActivity, request);
            state.SectionRefs[section] = response.SectionRef;
        }
    }

    /// <summary>Marks every section in <paramref name="invalidSet"/> <see cref="ArtifactStatus.Regenerating"/> (doc 03 §9, doc 06 §6) ahead of <see cref="RegenerateDownstreamAsync"/>.</summary>
    internal static void MarkInvalidSetRegenerating(GraphExecutionState state, IReadOnlyList<string> invalidSet)
    {
        foreach (var section in invalidSet)
        {
            state.ArtifactStatuses[section] = ArtifactStatus.Regenerating;
        }
    }
}
