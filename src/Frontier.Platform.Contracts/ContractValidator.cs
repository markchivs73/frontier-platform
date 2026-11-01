using System.Collections.Concurrent;
using System.Text.Json;

using Frontier.Platform.Abstractions;

using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The one validator (ADR-E27 D8). Each contract is compiled once, keyed by its hash, and the hash is
/// recomputed from the snapshot's content on every call, so a snapshot whose content was altered
/// after it was hashed is refused rather than validated against a stale compilation. The budgets are
/// the caller's, per call (ADR-PA43); a call without them uses <see cref="ContractsOptions"/>.
/// </summary>
internal sealed class ContractValidator(ISchemaEngine engine, IRuleEngine ruleEngine, IOptions<ContractsOptions> options) : IContractValidator
{
    private readonly ConcurrentDictionary<string, Lazy<CompiledContract>> _compiled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload) =>
        Validate(contract, utf8Payload, ConfiguredBudgets.From(options.Value));

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload) =>
        Validate(contract, payload, ConfiguredBudgets.From(options.Value));

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload, ContractBudgets budgets)
    {
        Checked(contract, budgets);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Payload, new JsonDocumentOptions { MaxDepth = budgets.MaxPayloadDepth });
        }
        catch (JsonException)
        {
            return new ContractValidationResult([NotJson(budgets)]);
        }

        using (document)
        {
            return Validate(contract, document.RootElement, budgets);
        }
    }

    /// <inheritdoc />
    public ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload, ContractBudgets budgets)
    {
        Checked(contract, budgets);
        var compiled = CompiledFor(contract);
        if (PayloadDepth.Of(payload) > budgets.MaxPayloadDepth)
        {
            return new ContractValidationResult([NotJson(budgets)]);
        }

        var violations = compiled.Schema.Validate(payload.GetRawText(), budgets.MaxViolations);
        if (violations.Count == 0)
        {
            violations = compiled.Rules.Evaluate(payload, budgets.MaxViolations, budgets.MaxRuleIterations);
        }

        return violations.Count == 0 ? ContractValidationResult.Valid : new ContractValidationResult(violations);
    }

    /// <summary>The compilation for <paramref name="contract"/>, after checking its hash against its content.</summary>
    internal CompiledContract CompiledFor(ContractSnapshot contract)
    {
        if (!string.Equals(ContractHash.Compute(contract.Schema, contract.Rules), contract.Hash, StringComparison.Ordinal))
        {
            throw new ContractViolationException(nameof(ContractSnapshot), [$"{contract.ContractRef}: the hash does not match the schema and rules it was pinned with"]);
        }

        return _compiled.GetOrAdd(contract.Hash, hash => new Lazy<CompiledContract>(() => Compile(hash, contract))).Value;
    }

    /// <summary>
    /// Compiles a pinned contract's schema and rules. Registration compiled both already, so a
    /// failure here is a permanent violation, never a retry. The cost ceiling was checked at
    /// registration and is not checked again (ADR-E27 D2): the compilation is keyed by hash alone, so
    /// it holds no budget, and a rule registered under one ceiling still runs after it is lowered.
    /// </summary>
    internal CompiledContract Compile(string hash, ContractSnapshot contract)
    {
        if (!engine.TryCompile(hash, contract.Schema, out var schema, out var error))
        {
            throw Refused(contract, "the schema does not compile: " + SchemaViolations.Bound(error));
        }

        var rules = ruleEngine.Compile(contract.Schema, contract.Rules, maxRuleCost: null);
        return rules.Rules is not null
            ? new CompiledContract(schema, rules.Rules)
            : throw Refused(contract, "a rule does not compile: " + rules.Findings[0].Message);
    }

    private static ContractViolationException Refused(ContractSnapshot contract, string reason) =>
        new(nameof(ContractSnapshot), [$"{contract.ContractRef}: {reason}"]);

    private static ContractViolation NotJson(ContractBudgets budgets) =>
        new(string.Empty, "json", $"The payload is not valid JSON, or nests deeper than {budgets.MaxPayloadDepth} levels.");

    /// <summary>Refuses a missing contract, and a missing or out-of-range record, before either is used.</summary>
    private static void Checked(ContractSnapshot contract, ContractBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ConfiguredBudgets.EnsureValid(budgets);
    }
}

/// <summary>A pinned contract's compiled schema and rules.</summary>
internal sealed record CompiledContract(ICompiledSchema Schema, ICompiledRules Rules);
