using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The JSON Schema port (ADR-E27 D3, TD-18). The one adapter is <see cref="CorvusSchemaEngine"/>;
/// nothing outside it sees a Corvus type.
/// </summary>
internal interface ISchemaEngine
{
    /// <summary>Where <paramref name="schema"/> breaks the JSON Schema 2020-12 meta-schema, at most <paramref name="maxViolations"/>.</summary>
    IReadOnlyList<ContractViolation> MetaSchemaViolations(JsonElement schema, int maxViolations);

    /// <summary>
    /// Compiles <paramref name="schema"/>, identified by its contract <paramref name="hash"/>. When it
    /// does not compile, <paramref name="error"/> is the engine's message, which quotes only the schema.
    /// </summary>
    bool TryCompile(string hash, JsonElement schema, [NotNullWhen(true)] out ICompiledSchema? compiled, [NotNullWhen(false)] out string? error);
}

/// <summary>A compiled schema. Thread-safe.</summary>
internal interface ICompiledSchema
{
    /// <summary>The violations of <paramref name="json"/>, at most <paramref name="maxViolations"/>; empty when it is valid.</summary>
    IReadOnlyList<ContractViolation> Validate(string json, int maxViolations);
}
