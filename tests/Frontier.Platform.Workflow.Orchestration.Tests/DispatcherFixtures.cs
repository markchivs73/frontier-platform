using System.Text.Json;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Shared S13.22 dispatcher fixtures: the pinned dispatcher input and a well-formed work-item payload.</summary>
internal static class DispatcherFixtures
{
    /// <summary>The dispatcher's own run id, preserved across every generation boundary (ADR-EX1).</summary>
    internal const string DispatcherRunId = "11111111-1111-1111-1111-111111111111";

    /// <summary>The dynamic-context epoch the Host resolved before scheduling (S13.60) — carried onto every child.</summary>
    internal const int Epoch = 4;

    /// <summary>The store's content hash of <see cref="Epoch"/>.</summary>
    internal const string EpochHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>A pinned dispatcher-mode input as the Host factory mints it.</summary>
    internal static GraphOrchestratorInput Input(WorkflowDefinition? definition = null) => new()
    {
        Definition = definition ?? OrchestrationFixtures.DispatcherModeChain(),
        EngagementId = "E2E::Acme::HQ",
        InitiatedBy = "user:oid-dispatcher-starter",
        RunId = DispatcherRunId,
        DynamicContextEpoch = Epoch,
        DynamicContextHash = EpochHash,
    };

    /// <summary>A work item carrying an ADR-E2 typed envelope.</summary>
    internal static WorkItem Item(string workItemId, string? directedBy = null) => new()
    {
        WorkItemId = workItemId,
        Payload = Payload(),
        DirectedBy = directedBy,
    };

    /// <summary>
    /// Registers the S13.18 rollover activity's handler on <paramref name="context"/> — fixture
    /// setup, not an assertion.
    /// <para>
    /// Any dispatcher run that reaches the generation boundary calls
    /// <see cref="WorkflowActivityNames.ResolveDispatcherVersionActivity"/>, and a bare
    /// <see cref="FakeTaskOrchestrationContext"/> throws for an activity with no handler. A test
    /// whose <c>WorkItem</c> is a plain value reaches that boundary whether or not it is about
    /// rollover, because the fake re-delivers a plain value on every wait.
    /// </para>
    /// <para>
    /// One shared registration rather than one per call site: three near-identical copies of a
    /// handler drift, and the drift would be silent.
    /// </para>
    /// </summary>
    /// <param name="context">The context to register on.</param>
    /// <param name="rollover">The definition the activity resolves to; null means "no successor".</param>
    /// <param name="recorded">Optional sink capturing each request, for tests asserting on the rollover itself.</param>
    internal static FakeTaskOrchestrationContext WithVersionResolver(
        this FakeTaskOrchestrationContext context,
        WorkflowDefinition? rollover = null,
        List<ResolveDispatcherVersionRequest>? recorded = null)
    {
        context.ActivityHandlers[WorkflowActivityNames.ResolveDispatcherVersionActivity] = input =>
        {
            recorded?.Add((ResolveDispatcherVersionRequest)input!);
            return new ResolveDispatcherVersionResult { Definition = rollover };
        };

        return context;
    }

    /// <summary>A small inline ADR-E2 envelope — the shape external ingest hands the dispatcher.</summary>
    internal static TypedPayload Payload() => new()
    {
        SchemaRef = "schemas/helpdesk-ticket/1.0",
        Payload = Json("""{"ticket_id":"HDT-1234","priority":"high"}"""),
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
