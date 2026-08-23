using System.Text.Json.Serialization;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.7c: the deterministic tool-call pipeline behind <c>InvokeMcpToolActivity</c> —
/// argument mapping from the upstream payload, the reserved idempotency-key argument,
/// canonical result capture, and simulated-ack detection.
/// </summary>
public sealed class McpToolInvocationPipelineTests
{
    [Fact]
    public void BuildArguments_MapsTopLevelWireFields_AndAddsIdempotencyKeyForWrites()
    {
        var input = Input() with
        {
            InputPayload = """{"ticket_id":"T-1","status":"resolved","count":3}""",
            IdempotencyKey = "eng-1::wf::t-update::0",
        };

        var arguments = McpToolInvocationPipeline.BuildArguments(input);

        Assert.Equal("T-1", ((JsonElement)arguments["ticket_id"]!).GetString());
        Assert.Equal(3, ((JsonElement)arguments["count"]!).GetInt32());
        Assert.Equal("eng-1::wf::t-update::0", arguments[McpToolInvocationPipeline.IdempotencyKeyArgument]);
    }

    [Fact]
    public void BuildArguments_NoPayloadNoKey_YieldsEmpty()
    {
        Assert.Empty(McpToolInvocationPipeline.BuildArguments(Input()));
    }

    [Fact]
    public void BuildArguments_NonObjectPayload_YieldsOnlyTheKey()
    {
        var input = Input() with { InputPayload = "\"just-a-string\"", IdempotencyKey = "k-1" };

        var arguments = McpToolInvocationPipeline.BuildArguments(input);

        Assert.Single(arguments);
        Assert.Equal("k-1", arguments[McpToolInvocationPipeline.IdempotencyKeyArgument]);
    }

    [Fact]
    public void Canonicalize_CoversResultShapes()
    {
        Assert.Equal("null", McpToolInvocationPipeline.Canonicalize(null));
        Assert.Equal("\"ok\"", McpToolInvocationPipeline.Canonicalize("ok"));
        Assert.Equal("""{"n":1}""", McpToolInvocationPipeline.Canonicalize(JsonDocument.Parse("""{"n":1}""").RootElement.Clone()));
        // The sandbox fence's ack serializes to its documented shape and is detected.
        var ack = McpToolInvocationPipeline.Canonicalize(new SimulatedAck(true, "com.example.crm/tickets/update_ticket"));
        Assert.Contains("\"simulated\":true", ack, StringComparison.Ordinal);
        Assert.True(McpToolInvocationPipeline.IsSimulatedAck(ack));
        Assert.False(McpToolInvocationPipeline.IsSimulatedAck("""{"ok":true}"""));
        Assert.False(McpToolInvocationPipeline.IsSimulatedAck("\"scalar\""));
    }

    [Fact]
    public async Task RunAsync_InvokesResolvedTool_AndReturnsCanonicalHashedResult()
    {
        AIFunctionArguments? seen = null;
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(new StubTool(arguments =>
        {
            seen = arguments;
            return JsonDocument.Parse("""{"updated":true}""").RootElement.Clone();
        })));

        var result = await pipeline.RunAsync(Input() with
        {
            InputPayload = """{"ticket_id":"T-1"}""",
            IdempotencyKey = "key-1",
        }, CancellationToken.None);

        Assert.Equal("T-1", ((JsonElement)seen!["ticket_id"]!).GetString());
        Assert.Equal("key-1", seen[McpToolInvocationPipeline.IdempotencyKeyArgument]);
        Assert.Equal("""{"updated":true}""", result.OutputPayload);
        Assert.Equal(CanonicalProfile.Hash("""{"updated":true}"""), result.OutputHash);
        Assert.False(result.Simulated);
        Assert.Equal("t-update", result.NodeId);
        Assert.Equal("com.example.crm/tickets/update_ticket", result.ToolRef);
        Assert.NotNull(result.HostBuild);
    }

    [Fact]
    public async Task RunAsync_SimulatedAck_IsFlagged()
    {
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(new StubTool(_ =>
            new SimulatedAck(true, "com.example.crm/tickets/update_ticket"))));

        var result = await pipeline.RunAsync(Input(), CancellationToken.None);

        Assert.True(result.Simulated);
    }

    [Fact]
    public async Task RunAsync_UnresolvableTool_Throws()
    {
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(tool: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(Input(), CancellationToken.None));
    }

    private static McpToolActivityInput Input() => new()
    {
        NodeId = "t-update",
        ArtifactKey = "ticket-update",
        ToolRef = "com.example.crm/tickets/update_ticket",
        TimeoutSeconds = 30,
        CorrelationId = "eng-1::wf::t-update::0",
        ExecutionId = "eng-1::wf",
        EngagementId = "eng-1",
    };

    private sealed class StubCatalog(AITool? tool) : IMcpToolCatalog
    {
        public Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, string executionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AITool>>(tool is null ? [] : [tool]);
    }

    private sealed class StubTool(Func<AIFunctionArguments, object?> implementation) : AIFunction
    {
        public override string Name => "stub-tool";

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult(implementation(arguments));
    }

    /// <summary>
    /// Stands in for the consumer's sandbox ack. The engine recognises the fence by the
    /// <c>simulated</c> property's shape, never by a type it owns, so the test asserts against
    /// that shape rather than importing a component that stays subsystem-side.
    /// </summary>
    private sealed record SimulatedAck(
        [property: JsonPropertyName("simulated")] bool Simulated,
        [property: JsonPropertyName("tool")] string Tool);
}
