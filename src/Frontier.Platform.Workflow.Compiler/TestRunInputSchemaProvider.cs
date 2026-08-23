using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;

using Frontier.Platform.Workflow.Compiler.Rules;
using Microsoft.Extensions.AI;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Supplies the entry node's declared input contract type's JSON Schema plus a valid example
/// skeleton payload (doc 19 §A4-R2, S9.43/C-31) — backs the A4 sandbox test-run screen's
/// "Expected shape" panel and "Use example" prefill. A cheaper, display-only alternative to
/// the originally-specced fully schema-driven generated form (C-31): the free-text textarea
/// stays the real input control, this only tells the tester what to type. The schema itself is
/// the plain <c>AIJsonUtilities</c> output with no converter-aware canonicalization (unlike
/// Orchestration's <c>CanonicalOutputSchema</c>, which exists for a different consumer — the
/// Anthropic structured-output request — and cannot be referenced from here, library-boundaries
/// forbids one subsystem library referencing another); a decimal/date/smart-enum field shows
/// unconstrained in the schema, but <see cref="ExampleSkeletonBuilder"/>'s companion example
/// always shows its correct concrete wire form regardless, which is where correctness matters
/// most for a tester copy-pasting a starting payload.
/// </summary>
public interface ITestRunInputSchemaProvider
{
    /// <summary>
    /// Returns the schema for <paramref name="definition"/>'s single control-graph entry node,
    /// or <see langword="null"/> if the graph has no single resolvable <see cref="AgentTaskNode"/>
    /// entry — callers fall back to the unaided free-text textarea.
    /// </summary>
    TestRunInputSchema? GetInputSchema(WorkflowDefinition definition);
}

/// <summary>The entry contract's JSON Schema and a valid example skeleton, both in canonical wire form.</summary>
public sealed record TestRunInputSchema(string ContractTypeName, JsonNode Schema, JsonNode Example);

/// <inheritdoc cref="ITestRunInputSchemaProvider" />
internal sealed class TestRunInputSchemaProvider : ITestRunInputSchemaProvider
{
    private readonly IContractTypeCatalog _catalog;

    public TestRunInputSchemaProvider(IContractTypeCatalog catalog) => _catalog = catalog;

    /// <inheritdoc />
    public TestRunInputSchema? GetInputSchema(WorkflowDefinition definition)
    {
        var entryIds = ControlGraphWalker.FindEntryNodeIds(definition);
        if (entryIds.Count != 1) return null;

        if (definition.Nodes.FirstOrDefault(n => n.NodeId == entryIds[0]) is not AgentTaskNode entryNode) return null;

        var contractType = _catalog.Resolve(entryNode.InputContractType);
        if (contractType is null) return null;

        var schemaElement = AIJsonUtilities.CreateJsonSchema(contractType, serializerOptions: CanonicalProfile.Options);
        var schema = JsonNode.Parse(schemaElement.GetRawText())!;
        var example = ExampleSkeletonBuilder.Build(contractType);
        return new TestRunInputSchema(entryNode.InputContractType, schema, example);
    }
}
