using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Picks the value an output contract is held to out of an MCP tool's reply (ADR-PA41). A tool that
/// declares an <c>outputSchema</c> answers in the <c>CallToolResult</c>'s <c>structuredContent</c>
/// (MCP 2025-06-18, Tools, "Structured Content" and "Output Schema"), and the consumer's tool adapter
/// hands the engine that whole result as JSON. The engine reads the result's JSON shape by its wire
/// names, never an SDK type, so the consumer's adapter stays its own.
/// </summary>
internal static class McpStructuredResult
{
    /// <summary>The <c>CallToolResult</c> member that carries the tool's structured result.</summary>
    internal const string StructuredContentMember = "structuredContent";

    /// <summary>The <c>CallToolResult</c> member that marks a tool-level error.</summary>
    internal const string IsErrorMember = "isError";

    /// <summary>
    /// The canonical JSON of <paramref name="reply"/>'s <c>structuredContent</c>, which is what a node
    /// that pins <paramref name="contract"/> records and validates. A result marked <c>isError</c> is
    /// returned whole, as before this change, so it is validated as it always was.
    /// </summary>
    /// <exception cref="ContractViolationException">The reply carries no <c>structuredContent</c>; this is permanent.</exception>
    internal static string Output(string reply, ContractSnapshot contract)
    {
        using var document = JsonDocument.Parse(reply);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(IsErrorMember, out var isError)
            && isError.ValueKind == JsonValueKind.True)
        {
            return reply;
        }

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(StructuredContentMember, out var structured)
            && structured.ValueKind != JsonValueKind.Null
                ? JsonSerializer.Serialize(structured, CanonicalProfile.Options)
                : throw Missing(contract);
    }

    /// <summary>The permanent refusal for a reply with no structured result to hold to <paramref name="contract"/>.</summary>
    private static ContractViolationException Missing(ContractSnapshot contract) => new(
        contract.ContractRef,
        [$"/: {StructuredContentMember}: the tool's result has no structuredContent, so it cannot be held to output contract '{contract.ContractRef}'; a tool that declares an outputSchema must return its result in structuredContent (MCP 2025-06-18, Tools, Structured Content)"]);
}
