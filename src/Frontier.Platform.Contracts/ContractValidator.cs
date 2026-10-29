using System.Collections.Concurrent;
using System.Text.Json;

using Frontier.Platform.Abstractions;

using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The one validator (ADR-E27 D8). Each contract is compiled once, keyed by its hash, and the hash is
/// recomputed from the snapshot's content on every call, so a snapshot whose content was altered
/// after it was hashed is refused rather than validated against a stale compilation.
/// </summary>
internal sealed class ContractValidator(ISchemaEngine engine, IOptions<ContractsOptions> options) : IContractValidator
{
    private readonly ConcurrentDictionary<string, Lazy<ICompiledSchema>> _compiled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload)
    {
        ArgumentNullException.ThrowIfNull(contract);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Payload, new JsonDocumentOptions { MaxDepth = options.Value.MaxPayloadDepth });
        }
        catch (JsonException)
        {
            return new ContractValidationResult([NotJson()]);
        }

        using (document)
        {
            return Validate(contract, document.RootElement);
        }
    }

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var compiled = CompiledFor(contract);
        if (PayloadDepth.Of(payload) > options.Value.MaxPayloadDepth)
        {
            return new ContractValidationResult([NotJson()]);
        }

        var violations = compiled.Validate(payload.GetRawText(), options.Value.MaxViolations);
        return violations.Count == 0 ? ContractValidationResult.Valid : new ContractValidationResult(violations);
    }

    /// <summary>The compilation for <paramref name="contract"/>, after checking its hash against its content.</summary>
    internal ICompiledSchema CompiledFor(ContractSnapshot contract)
    {
        if (!string.Equals(ContractHash.Compute(contract.Schema, contract.Rules), contract.Hash, StringComparison.Ordinal))
        {
            throw new ContractViolationException(nameof(ContractSnapshot), [$"{contract.ContractRef}: the hash does not match the schema and rules it was pinned with"]);
        }

        return _compiled.GetOrAdd(contract.Hash, hash => new Lazy<ICompiledSchema>(() => Compile(hash, contract))).Value;
    }

    /// <summary>Compiles a pinned contract. Registration compiled it already, so a failure here is a permanent violation, never a retry.</summary>
    internal ICompiledSchema Compile(string hash, ContractSnapshot contract) =>
        engine.TryCompile(hash, contract.Schema, out var compiled, out var error)
            ? compiled
            : throw new ContractViolationException(nameof(ContractSnapshot), [$"{contract.ContractRef}: the schema does not compile: {SchemaViolations.Bound(error)}"]);

    private ContractViolation NotJson() =>
        new(string.Empty, "json", $"The payload is not valid JSON, or nests deeper than {options.Value.MaxPayloadDepth} levels.");
}
