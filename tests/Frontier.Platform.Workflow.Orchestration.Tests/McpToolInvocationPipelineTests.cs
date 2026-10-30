using System.Text.Json.Serialization;
using System.Text.Json;
using Frontier.Platform.Abstractions;
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

    /// <summary>ADR-PA41: the arguments' hash is taken over the RFC 8785 form of what the tool received, key included, so payload key order does not move it.</summary>
    [Fact]
    public async Task RunAsync_RecordsTheJcsHashOfTheArgumentsTheToolReceived()
    {
        var pipeline = new McpToolInvocationPipeline(
            new StubCatalog(new StubTool(_ => JsonDocument.Parse("""{"updated":true}""").RootElement.Clone())),
            TestContractValidator.Instance);

        var result = await pipeline.RunAsync(Input() with { InputPayload = """{"b":1,"a":"x"}""", IdempotencyKey = "k-1" }, CancellationToken.None);

        var expected = JsonCanonicalizer.Hash("""{"a":"x","b":1,"idempotency_key":"k-1"}"""u8);
        Assert.Equal(expected, result.InputHash);
    }

    [Fact]
    public async Task RunAsync_InvokesResolvedTool_AndReturnsCanonicalHashedResult()
    {
        AIFunctionArguments? seen = null;
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(new StubTool(arguments =>
        {
            seen = arguments;
            return JsonDocument.Parse("""{"updated":true}""").RootElement.Clone();
        })), TestContractValidator.Instance);

        var result = await pipeline.RunAsync(Input() with
        {
            InputPayload = """{"ticket_id":"T-1"}""",
            IdempotencyKey = "key-1",
        }, CancellationToken.None);

        Assert.Equal("T-1", ((JsonElement)seen!["ticket_id"]!).GetString());
        Assert.Equal("key-1", seen[McpToolInvocationPipeline.IdempotencyKeyArgument]);
        Assert.Equal("""{"updated":true}""", result.OutputPayload);
        Assert.Equal(JsonCanonicalizer.Hash("""{"updated":true}"""u8), result.OutputHash);
        Assert.False(result.Simulated);
        Assert.Equal("t-update", result.NodeId);
        Assert.Equal("com.example.crm/tickets/update_ticket", result.ToolRef);
        Assert.NotNull(result.HostBuild);
    }

    [Fact]
    public async Task RunAsync_SameReplyInTwoKeyOrders_HashesIdentically_AndKeepsTheToolsOrder()
    {
        var first = await RunReturningAsync("""{"status":"resolved","ticket":{"id":"T-1","rev":2}}""");
        var second = await RunReturningAsync("""{"ticket":{"rev":2,"id":"T-1"},"status":"resolved"}""");

        Assert.Equal(first.OutputHash, second.OutputHash);
        Assert.Equal("""{"status":"resolved","ticket":{"id":"T-1","rev":2}}""", first.OutputPayload);
        Assert.Equal("""{"ticket":{"rev":2,"id":"T-1"},"status":"resolved"}""", second.OutputPayload);
    }

    [Fact]
    public async Task RunAsync_SimulatedAck_IsFlagged()
    {
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(new StubTool(_ =>
            new SimulatedAck(true, "com.example.crm/tickets/update_ticket"))), TestContractValidator.Instance);

        var result = await pipeline.RunAsync(Input(), CancellationToken.None);

        Assert.True(result.Simulated);
    }

    [Fact]
    public async Task RunAsync_UnresolvableTool_Throws()
    {
        var pipeline = new McpToolInvocationPipeline(new StubCatalog(tool: null), TestContractValidator.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(Input(), CancellationToken.None));
    }

    /// <summary>ADR-E27 D8: the arguments the tool receives, idempotency key included, are validated against the pinned input contract before the call.</summary>
    [Fact]
    public async Task RunAsync_ArgumentsThatFailTheInputContract_AreRefusedBeforeTheCall()
    {
        var called = false;
        var pipeline = Pipeline(_ => { called = true; return null; });
        var args = RegistryContractFixtures.Snapshot("crm/args/1.0",
            """{"type":"object","description":"Args.","properties":{"ticket_id":{"type":"string","description":"Id."}},"required":["ticket_id"],"additionalProperties":false}""");

        var refused = await Assert.ThrowsAsync<ContractViolationException>(() => pipeline.RunAsync(
            Input() with { InputPayload = """{"ticket_id":"T-1"}""", IdempotencyKey = "key-1", InputContract = args }, CancellationToken.None));

        Assert.Equal("crm/args/1.0", refused.ContractType);
        Assert.False(called);
    }

    [Fact]
    public async Task RunAsync_ValidArgumentsAndResult_RecordBothContractHashes()
    {
        var pipeline = Pipeline(_ => Reply(CallToolResult("""{"id":"T-1","priority":2}""")));
        var args = RegistryContractFixtures.Snapshot("crm/args/1.0",
            """{"type":"object","description":"Args.","properties":{"ticket_id":{"type":"string","description":"Id."},"idempotency_key":{"type":"string","description":"Key."}},"required":["ticket_id"],"additionalProperties":false}""");

        var result = await pipeline.RunAsync(
            Input() with { InputPayload = """{"ticket_id":"T-1"}""", IdempotencyKey = "key-1", InputContract = args, OutputContract = RegistryContractFixtures.Ticket },
            CancellationToken.None);

        Assert.Equal(args.Hash, result.InputContractHash);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, result.OutputContractHash);
    }

    [Fact]
    public async Task RunAsync_AResultThatFailsTheOutputContract_IsAContractViolation()
    {
        var pipeline = Pipeline(_ => Reply(CallToolResult("""{"id":"T-1"}""")));

        await Assert.ThrowsAsync<ContractViolationException>(() =>
            pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None));
    }

    /// <summary>
    /// ADR-PA41: a tool that declares an outputSchema answers in structuredContent (MCP 2025-06-18), and the
    /// adapter hands over the whole CallToolResult. The step records structuredContent alone, hashed over its JCS form.
    /// </summary>
    [Fact]
    public async Task RunAsync_APinnedOutputContract_RecordsAndValidatesStructuredContent()
    {
        var pipeline = Pipeline(_ => Reply(CallToolResult("""{"priority":2,"id":"T-1"}""")));

        var result = await pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None);

        Assert.Equal("""{"priority":2,"id":"T-1"}""", result.OutputPayload);
        Assert.Equal(JsonCanonicalizer.Hash("""{"id":"T-1","priority":2}"""u8), result.OutputHash);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, result.OutputContractHash);
        Assert.False(result.Simulated);
    }

    /// <summary>ADR-PA41: with an output contract pinned, a reply with no structured result is a permanent contract violation that says why.</summary>
    [Theory]
    [InlineData("""{"content":[{"type":"text","text":"{\"id\":\"T-1\",\"priority\":2}"}]}""")]
    [InlineData("""{"content":[],"structuredContent":null}""")]
    [InlineData("""{"id":"T-1","priority":2}""")]
    [InlineData("\"T-1\"")]
    public async Task RunAsync_APinnedOutputContract_WithoutStructuredContent_IsAPermanentViolation(string reply)
    {
        var pipeline = Pipeline(_ => Reply(reply));

        var refused = await Assert.ThrowsAsync<ContractViolationException>(() =>
            pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None));

        Assert.Equal("acme/ticket/1.0", refused.ContractType);
        Assert.Contains("has no structuredContent", Assert.Single(refused.Violations), StringComparison.Ordinal);
    }

    /// <summary>A tool that returns nothing at all has no structured result either.</summary>
    [Fact]
    public async Task RunAsync_APinnedOutputContract_NullReply_IsAPermanentViolation()
    {
        var pipeline = Pipeline(_ => null);

        await Assert.ThrowsAsync<ContractViolationException>(() =>
            pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None));
    }

    /// <summary>An isError result keeps today's handling: the whole result is held to the pinned contract, structuredContent or not.</summary>
    [Fact]
    public async Task RunAsync_AnIsErrorResult_IsHeldWholeToThePinnedContract()
    {
        var pipeline = Pipeline(_ => Reply("""{"content":[{"type":"text","text":"boom"}],"structuredContent":{"id":"T-1","priority":2},"isError":true}"""));

        var refused = await Assert.ThrowsAsync<ContractViolationException>(() =>
            pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None));

        Assert.DoesNotContain(refused.Violations, v => v.Contains("has no structuredContent", StringComparison.Ordinal));
    }

    /// <summary>An isError: false result is an ordinary reply, so its structuredContent is what is recorded.</summary>
    [Fact]
    public async Task RunAsync_AnIsErrorFalseResult_RecordsStructuredContent()
    {
        var pipeline = Pipeline(_ => Reply("""{"content":[],"structuredContent":{"id":"T-1","priority":2},"isError":false}"""));

        var result = await pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None);

        Assert.Equal("""{"id":"T-1","priority":2}""", result.OutputPayload);
    }

    /// <summary>With no output contract pinned, the whole CallToolResult is recorded exactly as before.</summary>
    [Fact]
    public async Task RunAsync_NoOutputContract_RecordsTheWholeResultUnchanged()
    {
        var whole = CallToolResult("""{"id":"T-1","priority":2}""");
        var pipeline = Pipeline(_ => Reply(whole));

        var result = await pipeline.RunAsync(Input(), CancellationToken.None);

        Assert.Equal(whole, result.OutputPayload);
        Assert.Equal(JsonCanonicalizer.Hash(System.Text.Encoding.UTF8.GetBytes(whole)), result.OutputHash);
        Assert.Null(result.OutputContractHash);
    }

    /// <summary>A write-fenced call returns the sandbox's ack, not the tool's reply, so the output contract does not apply.</summary>
    [Fact]
    public async Task RunAsync_ASimulatedAck_IsNotHeldToTheOutputContract()
    {
        var pipeline = Pipeline(_ => new SimulatedAck(true, "com.example.crm/tickets/update_ticket"));

        var result = await pipeline.RunAsync(Input() with { OutputContract = RegistryContractFixtures.Ticket }, CancellationToken.None);

        Assert.True(result.Simulated);
        Assert.Null(result.OutputContractHash);
    }

    [Fact]
    public void Constructor_NullContractValidator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new McpToolInvocationPipeline(new StubCatalog(tool: null), null!));
    }

    [Fact]
    public void ArgumentsJson_IsTheArgumentObjectAsSent()
    {
        var arguments = McpToolInvocationPipeline.BuildArguments(Input() with { InputPayload = """{"ticket_id":"T-1"}""", IdempotencyKey = "key-1" });

        Assert.Equal("""{"ticket_id":"T-1","idempotency_key":"key-1"}""", McpToolInvocationPipeline.ArgumentsJson(arguments));
    }

    /// <summary>A conforming MCP 2025-06-18 CallToolResult for a tool with an outputSchema: the text block mirrors the structured content.</summary>
    private static string CallToolResult(string structured) =>
        $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(structured)}}}],"structuredContent":{{structured}}}""";

    private static JsonElement Reply(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static McpToolInvocationPipeline Pipeline(Func<AIFunctionArguments, object?> tool) =>
        new(new StubCatalog(new StubTool(tool)), TestContractValidator.Instance);

    private static Task<McpToolActivityResult> RunReturningAsync(string json) =>
        new McpToolInvocationPipeline(new StubCatalog(new StubTool(_ => JsonDocument.Parse(json).RootElement.Clone())), TestContractValidator.Instance)
            .RunAsync(Input(), CancellationToken.None);

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
        public Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, string engagementId, CancellationToken ct) =>
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
