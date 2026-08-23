using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The work behind <c>InvokeMcpToolActivity</c> (S13.7c, ADR-CD9/ADR-CD6-superseded-half):
/// resolve the node's one tool through <see cref="IMcpToolCatalog"/> (registry-backed
/// endpoints; sandbox write-fencing applies exactly as it does for agent-mediated calls),
/// map the upstream payload onto the tool's arguments by wire name, call it under the
/// node's timeout, and return the canonical-JSON result. Stateless-core-friendly: no
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

    /// <summary>Constructs the pipeline over the runtime tool catalog.</summary>
    public McpToolInvocationPipeline(IMcpToolCatalog toolCatalog)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        this.toolCatalog = toolCatalog;
    }

    /// <inheritdoc />
    public async Task<McpToolActivityResult> RunAsync(McpToolActivityInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tools = await toolCatalog.ResolveAsync([input.ToolRef], input.ExecutionId, ct).ConfigureAwait(false);
        if (tools is not [AIFunction function])
        {
            throw new InvalidOperationException($"Tool ref '{input.ToolRef}' did not resolve to an invocable tool.");
        }

        var arguments = BuildArguments(input);
        var result = await InvokeWithTimeoutAsync(function, arguments, input.TimeoutSeconds, ct).ConfigureAwait(false);
        var payload = Canonicalize(result);

        return new McpToolActivityResult
        {
            NodeId = input.NodeId,
            ArtifactKey = input.ArtifactKey,
            ToolRef = input.ToolRef,
            OutputPayload = payload,
            OutputHash = CanonicalProfile.Hash(payload),
            Simulated = IsSimulatedAck(payload),
            HostBuild = HostBuildInfo.Version,
        };
    }

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

    /// <summary>Invokes the tool under the node's per-invocation timeout (doc 10 §7's innermost tier; <c>timeouts.nesting</c> guarantees it fits the activity cap).</summary>
    [ExcludeFromCodeCoverage(Justification = "Timeout composition over the real invocation path — the cancellation race needs a live slow connector; covered by the S9.25-family gate tests, not unit tests.")]
    internal static async Task<object?> InvokeWithTimeoutAsync(AIFunction function, AIFunctionArguments arguments, int timeoutSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return await function.InvokeAsync(arguments, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes the tool's result to canonical JSON. A sandbox write-fence ack is recognised by
    /// shape rather than by type — <c>{"simulated":true,...}</c>, see <see cref="IsSimulatedAck"/> —
    /// because the component that produces it is the consumer's, not the engine's.
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
