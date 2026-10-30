using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The design agent's view of the registry's contracts (ADR-PA41; consumer register S5-1, S5-2):
/// for each contract, the text the consumer's contract panel shows — its ref, description, each
/// property's description, its examples and each rule's message — and, for a contract with
/// legibility gaps, what the registration lint found. Ordered by ref, so the text changes only when
/// a contract version is registered or retired.
/// </summary>
internal static class DesignerContractAnnotations
{
    /// <summary>The block's text when the registry holds no usable contract.</summary>
    internal const string NoContracts = "(none: the registry holds no contract a new publish may name)";

    /// <summary>What an empty section says.</summary>
    internal const string None = "(none)";

    /// <summary>Each distinct ref in <paramref name="contracts"/>, ordinally ordered; the first entry for a ref wins.</summary>
    internal static IReadOnlyList<ContractSnapshot> Ordered(IReadOnlyList<ContractSnapshot> contracts) =>
        [.. contracts.DistinctBy(c => c.ContractRef, StringComparer.Ordinal).OrderBy(c => c.ContractRef, StringComparer.Ordinal)];

    /// <summary>The annotations block for <paramref name="contracts"/>, already <see cref="Ordered"/>.</summary>
    internal static string Render(IReadOnlyList<ContractSnapshot> contracts) =>
        contracts.Count == 0 ? NoContracts : string.Join("\n", contracts.Select(Render));

    /// <summary>One contract's entry.</summary>
    internal static string Render(ContractSnapshot contract)
    {
        var gaps = ContractLegibility.Gaps(contract);
        List<string> lines =
        [
            $"- {contract.ContractRef}",
            $"  Description: {ContractLegibility.Description(contract) ?? None}",
            .. Section("Properties", ContractLegibility.Properties(contract).Select(p => $"{p.Location}: {p.Description ?? "(no description)"}")),
            .. Section("Examples", Examples(contract.Schema)),
            .. Section("Rules", contract.Rules.Select(Rule)),
        ];
        if (gaps.Count > 0)
        {
            lines.Add("  Annotation gaps (an MCP server supplied this contract, and the registry accepted it with these gaps; do not guess what an undescribed field means):");
            lines.AddRange(gaps.Select(gap => $"    {gap.Location}: {gap.Message}"));
        }

        return string.Join("\n", lines);
    }

    /// <summary>The tool refs whose tool has no output contract, so returns any JSON; or <see cref="None"/>.</summary>
    internal static string AnyJsonTools(IReadOnlyList<DesignerToolDescriptor> tools)
    {
        var refs = tools.Where(tool => tool.OutputContractRef is null).Select(tool => tool.ToolRef).ToList();
        return refs.Count == 0 ? None : string.Join(", ", refs);
    }

    /// <summary>A titled, indented list, saying <see cref="None"/> when it is empty.</summary>
    internal static IEnumerable<string> Section(string title, IEnumerable<string> items)
    {
        var entries = items.Select(item => "    " + item).ToList();
        return [$"  {title}:", .. entries.Count == 0 ? ["    " + None] : entries];
    }

    /// <summary>Each example under the schema's <c>examples</c>, as compact JSON.</summary>
    internal static IEnumerable<string> Examples(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array
            ? examples.EnumerateArray().Select(example => JsonSerializer.Serialize(example, CanonicalProfile.Options))
            : [];

    /// <summary>A rule as its field and message.</summary>
    internal static string Rule(ContractRule rule) =>
        $"{rule.FieldPath ?? "(whole payload)"}: {(string.IsNullOrWhiteSpace(rule.Message) ? "(no message)" : rule.Message)}";
}
