using System.Text;
using System.Text.Json;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The work behind <c>InvokeMcpToolActivity</c> (S13.7c, ADR-CD9/ADR-CD6-superseded-half):
/// resolve the node's one tool through <see cref="IMcpToolCatalog"/> on the node's pins (registry-backed
/// endpoints; sandbox write-fencing follows the pinned write flag, as it does for agent-mediated calls, ADR-PA42),
/// map the upstream payload onto the tool's arguments by wire name, call it under the
/// node's timeout, and return the canonical-JSON result. When the node pins an output contract,
/// the recorded output is the tool's <c>structuredContent</c>, validated against that contract
/// (ADR-PA41, see <see cref="McpStructuredResult"/>). Stateless-core-friendly: no
/// section/store writes here — the orchestrator records outputs through the same
/// activities agent steps use.
/// </summary>
public interface IMcpToolInvocationPipeline
{
    /// <summary>Runs one deterministic tool call.</summary>
    Task<McpToolActivityResult> RunAsync(McpToolActivityInput input, CancellationToken ct);
}

/// <inheritdoc cref="IMcpToolInvocationPipeline" />
internal sealed class McpToolInvocationPipeline : IMcpToolInvocationPipeline
{
    /// <summary>The reserved argument name a write call's idempotency key rides under (doc 00 §2.1's key spec, made concrete for MCP: connectors receive it as a top-level argument).</summary>
    internal const string IdempotencyKeyArgument = "idempotency_key";

    private readonly IMcpToolCatalog toolCatalog;
    private readonly IContractValidator contractValidator;
    private readonly IHostBuildStamp hostBuild;

    /// <summary>Constructs the pipeline over the runtime tool catalog, the contract validator and the worker's build stamp.</summary>
    public McpToolInvocationPipeline(IMcpToolCatalog toolCatalog, IContractValidator contractValidator, IHostBuildStamp hostBuild)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        ArgumentNullException.ThrowIfNull(contractValidator);
        ArgumentNullException.ThrowIfNull(hostBuild);
        this.toolCatalog = toolCatalog;
        this.contractValidator = contractValidator;
        this.hostBuild = hostBuild;
    }

    /// <inheritdoc />
    public async Task<McpToolActivityResult> RunAsync(McpToolActivityInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tools = await toolCatalog.ResolveAsync([input.ToolRef], [Pin(input)], input.EngagementId, ct).ConfigureAwait(false);
        if (tools is not [AIFunction function])
        {
            throw new InvalidOperationException($"Tool ref '{input.ToolRef}' did not resolve to an invocable tool.");
        }

        var arguments = BuildArguments(input);
        var argumentsJson = ArgumentsJson(arguments);
        if (input.InputContract is { } inputContract)
        {
            RegistryContractCheck.Ensure(contractValidator, inputContract, argumentsJson, input.ContractBudgets);
        }

        var result = await InvokeWithTimeoutAsync(function, arguments, input.TimeoutSeconds, ct).ConfigureAwait(false);
        var reply = Canonicalize(result);
        var simulated = IsSimulatedAck(reply);
        var outputContract = simulated ? null : input.OutputContract;
        var payload = reply;
        if (outputContract is not null)
        {
            payload = McpStructuredResult.Output(reply, outputContract);
            RegistryContractCheck.Ensure(contractValidator, outputContract, payload, input.ContractBudgets);
        }

        return new McpToolActivityResult
        {
            NodeId = input.NodeId,
            ArtifactKey = input.ArtifactKey,
            ToolRef = input.ToolRef,
            OutputPayload = payload,
            OutputHash = JsonCanonicalizer.Hash(Encoding.UTF8.GetBytes(payload)),
            Simulated = simulated,
            HostBuild = hostBuild.Version,
            InputContractHash = input.InputContract?.Hash,
            OutputContractHash = outputContract?.Hash,
            InputHash = JsonCanonicalizer.Hash(Encoding.UTF8.GetBytes(argumentsJson)),
        };
    }

    /// <summary>
    /// The node's pins for its one tool (ADR-PA42): the contract snapshots and write flag the definition
    /// pins, so the catalogue fences the tool by the pinned flag. A field the node does not pin stays
    /// null, and the catalogue resolves it as before.
    /// </summary>
    internal static McpToolPin Pin(McpToolActivityInput input) => new()
    {
        ToolRef = input.ToolRef,
        InputContract = input.InputContract,
        OutputContract = input.OutputContract,
        Writes = input.Writes,
    };

    /// <summary>
    /// Maps the upstream payload's top-level wire properties onto the tool's arguments;
    /// a write call's idempotency key rides as <see cref="IdempotencyKeyArgument"/>.
    /// A non-object payload (or none) yields only the key, if any.
    /// </summary>
    internal static AIFunctionArguments BuildArguments(McpToolActivityInput input)
    {
        var arguments = new AIFunctionArguments();
        if (input.InputPayload is { } payload)
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.Clone();
                }
            }
        }

        if (input.IdempotencyKey is { } key)
        {
            arguments[IdempotencyKeyArgument] = key;
        }

        return arguments;
    }

    /// <summary>
    /// The arguments exactly as the tool receives them, as a JSON object: the validated input of the
    /// call (ADR-E27 D8), including the idempotency key a write carries.
    /// </summary>
    internal static string ArgumentsJson(AIFunctionArguments arguments) =>
        JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal), CanonicalProfile.Options);

    /// <summary>Invokes the tool under the node's per-invocation timeout (doc 10 §7's innermost tier; <c>timeouts.nesting</c> guarantees it fits the activity cap).</summary>
    internal static async Task<object?> InvokeWithTimeoutAsync(AIFunction function, AIFunctionArguments arguments, int timeoutSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return await function.InvokeAsync(arguments, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes the tool's result to canonical JSON. A sandbox write-fence ack is recognised by
    /// shape rather than by type — <c>{"simulated":true,...}</c>, see <see cref="IsSimulatedAck"/> —
    /// because the component that produces it is the consumer's, not the engine's. The payload keeps
    /// the tool's own key order; only its hash is taken over the RFC 8785 form (ADR-E2 decision 2).
    /// </summary>
    internal static string Canonicalize(object? result) => result switch
    {
        null => "null",
        string text => JsonSerializer.Serialize(text, CanonicalProfile.Options),
        JsonElement element => JsonSerializer.Serialize(element, CanonicalProfile.Options),
        _ => JsonSerializer.Serialize(result, result.GetType(), CanonicalProfile.Options),
    };

    /// <summary>Whether <paramref name="payload"/> is the sandbox write-fence's simulated ack (doc 13 §5).</summary>
    internal static bool IsSimulatedAck(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("simulated", out var simulated)
            && simulated.ValueKind == JsonValueKind.True;
    }
}
