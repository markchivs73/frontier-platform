using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// What a registered contract tells a reader (ADR-E27 D10, ADR-PA41): its described properties and
/// its legibility gaps. Pure, and the same lint registration runs, so a design prompt or a contract
/// panel can never disagree with registration about what a gap is.
/// </summary>
public static class ContractLegibility
{
    /// <summary>
    /// The registration lint's findings for <paramref name="snapshot"/>, as warnings and in document
    /// order. Empty for an authored or generated contract, since registration refuses one with a gap;
    /// a discovered contract may have any number.
    /// </summary>
    public static IReadOnlyList<ContractFinding> Gaps(ContractSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ContractLint.Findings(snapshot.Schema, snapshot.Rules, ContractProvenance.Discovered);
    }

    /// <summary>
    /// Every property the schema declares under a <c>properties</c> keyword, at any depth and in
    /// document order, with its <c>description</c> when it has one. Locations are the lint's:
    /// <c>/schema</c> plus the RFC 6901 pointer to the property's subschema.
    /// </summary>
    public static IReadOnlyList<ContractPropertyAnnotation> Properties(ContractSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. SchemaWalk.All(snapshot.Schema)
            .Where(subschema => subschema.Position == SubschemaPosition.Property)
            .Select(subschema => new ContractPropertyAnnotation("/schema" + subschema.Pointer, Description(subschema.Schema)))];
    }

    /// <summary>The contract-level <c>description</c>: what the contract is for. Null when it has none.</summary>
    public static string? Description(ContractSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Description(snapshot.Schema);
    }

    /// <summary>The schema's non-blank <c>description</c>, or null.</summary>
    private static string? Description(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("description", out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}

/// <summary>One property a contract declares, and what it says about it.</summary>
/// <param name="Location"><c>/schema</c> plus the RFC 6901 pointer to the property's subschema, such as <c>/schema/properties/total</c>.</param>
/// <param name="Description">The property's <c>description</c>, or null when it has none.</param>
public sealed record ContractPropertyAnnotation(string Location, string? Description);
