using System.Security.Cryptography;
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
/// </summary>
internal static class OutputPayloadBuilder
{
    /// <summary>Serializes <paramref name="result"/> as <paramref name="outputType"/> and returns its canonical-JSON payload and SHA256 hex hash.</summary>
    internal static (string Payload, string Hash) Build(IVersionedContract result, Type outputType)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(outputType);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, outputType, CanonicalProfile.Options);
        var payload = Encoding.UTF8.GetString(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        return (payload, hash);
    }
}
