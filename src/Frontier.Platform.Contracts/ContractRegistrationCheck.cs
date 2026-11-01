using System.Text.Json;

using Frontier.Platform.Abstractions;

using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The registration check (ADR-E27 D4, D5, D10). A schema that leaves its own document is never
/// compiled, so a refused reference cannot make the engine fetch anything. The rule cost ceiling is
/// checked here, under the caller's budgets or the configured ones (ADR-PA43).
/// </summary>
internal sealed class ContractRegistrationCheck(ISchemaEngine engine, IRuleEngine ruleEngine, IOptions<ContractsOptions> options) : IContractRegistrationCheck
{
    /// <summary>The most meta-schema findings reported for one schema.</summary>
    internal const int MaxMetaSchemaFindings = 10;

    /// <inheritdoc />
    public ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<ContractSnapshot> registered) =>
        Check(request, registered, ConfiguredBudgets.From(options.Value));

    /// <inheritdoc />
    public ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<ContractSnapshot> registered, ContractBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registered);
        ConfiguredBudgets.EnsureValid(budgets);
        var rules = request.Rules ?? [];
        var findings = new List<ContractFinding>();
        if (!ContractRef.TryParse(request.ContractRef, out var contractRef))
        {
            findings.Add(new ContractFinding(ContractFindingCodes.RefMalformed, ContractFindingSeverity.Error, "/contract_ref",
                $"'{request.ContractRef}' is not a contract ref: expected '{{namespace}}/{{name}}/{{major}}.{{minor}}', such as 'frontier/evidence-summary/1.0'."));
        }

        if (request.Schema.ValueKind != JsonValueKind.Object)
        {
            findings.Add(new ContractFinding(ContractFindingCodes.SchemaNotObject, ContractFindingSeverity.Error, "/schema",
                "The schema must be a JSON Schema 2020-12 document: a JSON object."));
            return Result(request, rules, findings);
        }

        findings.AddRange(SchemaFindings(request.Schema, rules, request.Provenance, budgets));
        if (contractRef is not null)
        {
            findings.AddRange(ContractCompatibility.Findings(contractRef, request.Schema, rules, registered));
        }

        return Result(request, rules, findings);
    }

    /// <summary>References, the lint, compilation and the examples.</summary>
    internal IEnumerable<ContractFinding> SchemaFindings(JsonElement schema, IReadOnlyList<ContractRule> rules, ContractProvenance provenance, ContractBudgets budgets)
    {
        var references = SchemaReferences.Findings(schema).ToList();
        var lint = ContractLint.Findings(schema, rules, provenance);
        return references.Count > 0
            ? [.. references, .. lint]
            : [.. references, .. lint, .. CompiledFindings(schema, rules, provenance, budgets)];
    }

    /// <summary>
    /// Checks the schema against the 2020-12 meta-schema, compiles it and its rules, and runs every
    /// example through both. An example must satisfy the schema and every rule (Mark, 2026-10-29), so
    /// a rule that cannot hold for the contract's own examples is caught here, not at run time.
    /// </summary>
    internal IEnumerable<ContractFinding> CompiledFindings(JsonElement schema, IReadOnlyList<ContractRule> rules, ContractProvenance provenance, ContractBudgets budgets)
    {
        var malformed = engine.MetaSchemaViolations(schema, MaxMetaSchemaFindings);
        if (malformed.Count > 0)
        {
            return malformed.Select(violation => new ContractFinding(ContractFindingCodes.SchemaInvalid, ContractFindingSeverity.Error, "/schema" + violation.Location,
                $"This is not valid JSON Schema 2020-12: {violation.Message}")).ToList();
        }

        if (!engine.TryCompile(ContractHash.Compute(schema, rules), schema, out var compiled, out var error))
        {
            return [new ContractFinding(ContractFindingCodes.SchemaInvalid, ContractFindingSeverity.Error, "/schema",
                "The schema does not compile: " + SchemaViolations.Bound(error))];
        }

        var compiledRules = ruleEngine.Compile(schema, rules, budgets.MaxRuleCost);
        var severity = provenance.LintRefuses ? ContractFindingSeverity.Error : ContractFindingSeverity.Warning;
        return [.. compiledRules.Findings, .. ExampleFindings(schema, compiled, compiledRules.Rules, severity, budgets.MaxRuleIterations)];
    }

    /// <summary>
    /// A finding for each example the compiled schema refuses, naming its first violation, and for
    /// each rule a schema-valid example fails, each rule given <paramref name="maxIterations"/>. Rules
    /// are skipped when they did not compile.
    /// </summary>
    internal static IEnumerable<ContractFinding> ExampleFindings(JsonElement schema, ICompiledSchema compiled, ICompiledRules? rules, ContractFindingSeverity severity, int maxIterations)
    {
        if (!schema.TryGetProperty("examples", out var examples) || examples.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return examples.EnumerateArray()
            .SelectMany((example, index) => ExampleFindings(example, $"/schema/examples/{index}", compiled, rules, severity, maxIterations))
            .ToList();
    }

    private static List<ContractFinding> ExampleFindings(JsonElement example, string location, ICompiledSchema compiled, ICompiledRules? rules, ContractFindingSeverity severity, int maxIterations)
    {
        var violations = compiled.Validate(example.GetRawText(), 1);
        if (violations.Count > 0)
        {
            return [new ContractFinding(ContractFindingCodes.ExampleInvalid, severity, location,
                $"This example does not satisfy the contract: at '{violations[0].Location}', {violations[0].Message}")];
        }

        return (rules?.Evaluate(example, int.MaxValue, maxIterations) ?? [])
            .Select(failed => new ContractFinding(ContractFindingCodes.ExampleFailsRule, severity, location, $"This example fails a rule: {failed.Message}"))
            .ToList();
    }

    private static ContractRegistrationResult Result(ContractRegistrationRequest request, IReadOnlyList<ContractRule> rules, List<ContractFinding> findings)
    {
        var ordered = findings.Where(f => f.Severity == ContractFindingSeverity.Error)
            .Concat(findings.Where(f => f.Severity == ContractFindingSeverity.Warning))
            .ToList();
        var accepted = ordered.TrueForAll(f => f.Severity != ContractFindingSeverity.Error);
        return new ContractRegistrationResult(accepted ? Snapshot(request, rules) : null, ordered);
    }

    private static ContractSnapshot Snapshot(ContractRegistrationRequest request, IReadOnlyList<ContractRule> rules)
    {
        var schema = request.Schema.Clone();
        return new ContractSnapshot { ContractRef = request.ContractRef, Schema = schema, Rules = rules, Hash = ContractHash.Compute(schema, rules) };
    }
}
