using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mutable bookkeeping for the ADR-5 ready-set walk (S13.7i): which nodes are ready,
/// which are running, which completed, and the first permanent failure observed. The
/// async scheduling loop lives in <see cref="GraphOrchestratorSteps.RunInitialWalkAsync"/>;
/// everything here is synchronous and deterministic — ready-set iteration is
/// lexicographic (<see cref="SortedSet{T}"/> with ordinal comparison), matching
/// <see cref="GraphTopology"/>'s tie-breaking so scheduling order is a pure function of
/// completion history (dtf-determinism).
/// </summary>
internal sealed class GraphWalk
{
    private GraphWalk(
        IReadOnlyDictionary<string, WorkflowNode> nodesById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency,
        Dictionary<string, int> inDegree,
        SortedSet<string> ready,
        int nodeCount)
    {
        NodesById = nodesById;
        Adjacency = adjacency;
        InDegree = inDegree;
        Ready = ready;
        NodeCount = nodeCount;
    }

    /// <summary>Every node in the definition, keyed by <see cref="WorkflowNode.NodeId"/>.</summary>
    internal IReadOnlyDictionary<string, WorkflowNode> NodesById { get; }

    /// <summary>Successor node ids per node id, over every edge regardless of <see cref="EdgeKind"/> (<see cref="GraphTopology.BuildAdjacency"/>).</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Adjacency { get; }

    /// <summary>Remaining unsatisfied predecessor count per node id.</summary>
    internal Dictionary<string, int> InDegree { get; }

    /// <summary>Total inbound edge count per node id (unique predecessors), fixed at creation — the denominator for skip detection.</summary>
    internal Dictionary<string, int> TotalInbound { get; } = new(StringComparer.Ordinal);

    /// <summary>How many of each node's inbound edges are dead — on an unselected decision branch or from a skipped node (ADR-5 D6, S13.7j).</summary>
    internal Dictionary<string, int> DeadInbound { get; } = new(StringComparer.Ordinal);

    /// <summary>Nodes skipped because every inbound edge was dead, in skip order.</summary>
    internal List<string> SkippedNodeIds { get; } = [];

    /// <summary>Node ids whose predecessors have all completed, lexicographically ordered.</summary>
    internal SortedSet<string> Ready { get; }

    /// <summary>In-flight node tasks, keyed by node id.</summary>
    internal Dictionary<string, Task> Running { get; } = new(StringComparer.Ordinal);

    /// <summary>Total nodes in the definition — completing fewer indicates a cycle (see <see cref="ThrowIfIncomplete"/>).</summary>
    internal int NodeCount { get; }

    /// <summary>Nodes completed so far (agent nodes and gates alike).</summary>
    internal int CompletedCount { get; private set; }

    /// <summary>The first permanent failure observed, if any (ADR-5 Decision 4: no new node starts after this is set).</summary>
    internal Exception? FirstFailure { get; private set; }

    /// <summary>The node whose task faulted first — the real failing node for <c>paused_on_failure</c> attribution.</summary>
    internal string? FailedNodeId { get; private set; }

    /// <summary>Whether the walk still has ready or in-flight work.</summary>
    internal bool HasWork => Ready.Count > 0 || Running.Count > 0;

    /// <summary>Builds the walk bookkeeping for <paramref name="definition"/> from <see cref="GraphTopology"/>'s adjacency and in-degrees.</summary>
    internal static GraphWalk Create(WorkflowDefinition definition)
    {
        var nodesById = definition.Nodes.ToDictionary(node => node.NodeId, node => node, StringComparer.Ordinal);
        var nodeIds = definition.Nodes.Select(node => node.NodeId).ToList();
        var adjacency = GraphTopology.BuildAdjacency(definition.Edges, nodeIds);
        var inDegree = GraphTopology.ComputeInDegrees(adjacency, nodeIds);
        var ready = new SortedSet<string>(nodeIds.Where(id => inDegree[id] == 0), StringComparer.Ordinal);

        var walk = new GraphWalk(nodesById, adjacency, inDegree, ready, nodeIds.Count);
        foreach (var (nodeId, degree) in walk.InDegree)
        {
            walk.TotalInbound[nodeId] = degree;
        }

        return walk;
    }

    /// <summary>
    /// Removes and returns every ready non-gate node id, in lexicographic order — the
    /// concurrent frontier to schedule (ADR-5 Decision 1).
    /// </summary>
    internal IReadOnlyList<string> TakeReadyAgentNodes()
    {
        var agentNodes = Ready.Where(id => NodesById[id].NodeType != NodeType.HumanGate && NodesById[id].NodeType != NodeType.Decision).ToList();
        foreach (var id in agentNodes)
        {
            Ready.Remove(id);
        }

        return agentNodes;
    }

    /// <summary>
    /// Removes and returns the lexicographically first ready <see cref="Abstractions.DecisionNode"/>
    /// id, or null when none is ready. Decisions are evaluated inline (pure, no activity)
    /// and processed before agents each scheduling round, so an unselected subtree dies
    /// before anything in it could start (ADR-5 Decision 6, S13.7j).
    /// </summary>
    internal string? TakeReadyDecision()
    {
        var decisionId = Ready.FirstOrDefault(id => NodesById[id].NodeType == NodeType.Decision);
        if (decisionId is not null)
        {
            Ready.Remove(decisionId);
        }

        return decisionId;
    }

    /// <summary>
    /// Removes and returns the lexicographically first ready gate id, but only when the
    /// walk has fully quiesced — nothing running, no non-gate node ready (ADR-5
    /// Decision 2: gates are barriers; the approver reviews a consistent cut).
    /// </summary>
    internal string? TakeReadyGateWhenQuiesced()
    {
        if (Running.Count > 0 || Ready.Count == 0)
        {
            return null;
        }

        var gateId = Ready.Min!;
        Ready.Remove(gateId);
        return gateId;
    }

    /// <summary>Records <paramref name="nodeId"/> as completed and releases its successors into <see cref="Ready"/> (all outbound edges live).</summary>
    internal void Complete(string nodeId)
    {
        CompletedCount++;
        ReleaseSuccessors(nodeId, deadTargets: null);
    }

    /// <summary>
    /// Records a completed <see cref="Abstractions.DecisionNode"/> (ADR-5 Decision 6,
    /// S13.7j): the edge to <paramref name="selectedTargetId"/> is live; every other
    /// outbound edge is dead. A successor whose inbound edges are all dead is skipped,
    /// and its own outbound edges die too — the unselected subtree drains without running.
    /// </summary>
    internal void CompleteDecision(string nodeId, string selectedTargetId)
    {
        CompletedCount++;
        var deadTargets = new HashSet<string>(Adjacency[nodeId].Where(target => !string.Equals(target, selectedTargetId, StringComparison.Ordinal)), StringComparer.Ordinal);
        ReleaseSuccessors(nodeId, deadTargets);
    }

    /// <summary>
    /// Decrements each successor's in-degree (dead edges release ordering exactly like
    /// live ones); when a successor's last inbound resolves, it is skipped if every
    /// inbound was dead, else it becomes ready.
    /// </summary>
    internal void ReleaseSuccessors(string nodeId, IReadOnlySet<string>? deadTargets)
    {
        foreach (var target in Adjacency[nodeId])
        {
            if (deadTargets?.Contains(target) == true)
            {
                DeadInbound[target] = DeadInbound.GetValueOrDefault(target) + 1;
            }

            if (--InDegree[target] > 0)
            {
                continue;
            }

            if (DeadInbound.GetValueOrDefault(target) == TotalInbound[target])
            {
                Skip(target);
            }
            else
            {
                Ready.Add(target);
            }
        }
    }

    /// <summary>Records <paramref name="nodeId"/> as skipped (it counts toward completeness) and kills its outbound edges, cascading through its subtree.</summary>
    internal void Skip(string nodeId)
    {
        SkippedNodeIds.Add(nodeId);
        CompletedCount++;
        var allDead = new HashSet<string>(Adjacency[nodeId], StringComparer.Ordinal);
        ReleaseSuccessors(nodeId, allDead);
    }

    /// <summary>
    /// Records the first permanent failure (later sibling failures are absorbed — one
    /// attributed pause per execution). A failed node's successors are never released.
    /// </summary>
    internal void Fail(string nodeId, Exception exception)
    {
        if (FirstFailure is null)
        {
            FirstFailure = exception;
            FailedNodeId = nodeId;
        }
    }

    /// <summary>
    /// Removes every finished task from <see cref="Running"/> and returns them in
    /// lexicographic node-id order, so successor release is deterministic given the
    /// completion set.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, Task>> DrainFinished()
    {
        var finished = Running
            .Where(pair => pair.Value.IsCompleted)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var pair in finished)
        {
            Running.Remove(pair.Key);
        }

        return finished;
    }

    /// <summary>
    /// Throws if the walk drained without completing every node — with no recorded
    /// failure that means unreachable nodes, i.e. a cycle in a stored definition
    /// (corrupted-definition alarm, doc 03 §10; publish-time validation makes this
    /// unreachable for governed definitions).
    /// </summary>
    internal void ThrowIfIncomplete()
    {
        if (FirstFailure is null && CompletedCount != NodeCount)
        {
            throw new ContractViolationException(nameof(WorkflowDefinition), ["Node graph contains a cycle; cannot compute an execution order."]);
        }
    }
}
