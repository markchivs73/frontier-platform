using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Produces <see cref="AgentTaskActivityResult.OutputPayload"/>/<see cref="AgentTaskActivityResult.OutputHash"/>
/// (doc 00 §4.3 step 6, canonical-serialization). <see cref="CanonicalProfile.SerializeCanonical{T}"/>
/// and <see cref="CanonicalProfile.Hash{T}"/> are generic over the <em>static</em> type
/// <typeparamref name="T"/>; a value held as <see cref="IVersionedContract"/> would
/// serialize only the interface's members through those overloads. This builder instead
/// uses the <c>(object, Type, JsonSerializerOptions)</c> overload with the node's
/// concrete <see cref="AgentTaskNode.OutputContractType"/> (resolved by
/// <see cref="IContractTypeRegistry"/>) so the full record serializes.
/// <para>
/// The payload is the canonical profile's bytes, unchanged; the hash is taken over their RFC 8785
/// (JCS) form (ADR-E2 decision 2; ADR-PA30, amended 2026-10-24). <c>[JsonPropertyOrder]</c> fixes a
/// contract's own members, but a <see cref="System.Text.Json.JsonElement"/> riding inside one keeps
/// the order its producer chose, so without JCS two identical replies could hash differently.
/// </para>
/// </summary>
internal static class OutputPayloadBuilder
{
    /// <summary>Serializes <paramref name="result"/> as <paramref name="outputType"/> and returns its canonical-JSON payload and the SHA256 hex hash of its JCS form.</summary>
    internal static (string Payload, string Hash) Build(IVersionedContract result, Type outputType)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(outputType);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, outputType, CanonicalProfile.Options);
        var payload = Encoding.UTF8.GetString(bytes);
        var hash = JsonCanonicalizer.Hash(bytes);

        return (payload, hash);
    }
}
