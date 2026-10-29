using System.Text.Json;

using Celly;
using Celly.Checking;
using Celly.Interpreter;
using Celly.Types;
using Celly.Values;

using Frontier.Platform.Abstractions;

using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The Celly adapter (TD-17, Celly 1.2.0). Each rule is parsed, type-checked against the schema's
/// types with <c>self</c> as the payload, held to the profile, and costed against
/// <see cref="ContractsOptions.MaxRuleCost"/>; an unbounded estimate is refused, since a rule whose
/// cost the schema does not bound has no ceiling. At run time each rule gets
/// <see cref="ContractsOptions.MaxRuleIterations"/>, a count, never a clock (ADR-E27 D2).
/// </summary>
internal sealed class CellyRuleEngine(IOptions<ContractsOptions> options) : IRuleEngine
{
    private const string Self = "self";

    /// <inheritdoc />
    public RuleCompilation Compile(JsonElement schema, IReadOnlyList<ContractRule> rules)
    {
        var types = new CelSchemaTypes(schema);
        var env = CelEnv.Create(new CelEnvSettings
        {
            TypeProvider = types,
            Adapter = types,
            Declarations = [new VariableDecl(Self, types.SelfType)],
        });
        var programs = new List<(ContractRule Rule, CelProgram Program)>();
        var findings = new List<ContractFinding>();
        foreach (var (rule, index) in rules.Select((rule, index) => (rule, index)))
        {
            var (program, finding) = CompileRule(env, types, rule.Rule, $"/rules/{index}");
            if (finding is not null)
            {
                findings.Add(finding);
            }
            else
            {
                programs.Add((rule, program!));
            }
        }

        return findings.Count > 0 ? new RuleCompilation(null, findings) : new RuleCompilation(new Compiled(types, programs, options.Value.MaxRuleIterations), []);
    }

    /// <summary>Compiles one rule, or says why it cannot be used.</summary>
    internal (CelProgram? Program, ContractFinding? Finding) CompileRule(CelEnv env, CelSchemaTypes types, string expression, string location)
    {
        var parsed = env.Parse(expression ?? string.Empty);
        if (parsed.HasErrors || parsed.Ast is null)
        {
            return (null, Refusal(ContractFindingCodes.RuleInvalid, location, "The rule does not parse: " + FirstIssue(parsed.Issues)));
        }

        var checkedRule = env.Check(parsed.Ast);
        if (checkedRule.HasErrors)
        {
            return (null, Refusal(ContractFindingCodes.RuleInvalid, location, "The rule does not type-check against the schema: " + FirstIssue(checkedRule.Issues)));
        }

        var refusal = ProfileFinding(parsed.Ast.Expr, location)
            ?? ResultFinding(checkedRule.TypeOf(parsed.Ast.Expr), location)
            ?? CostFinding(env.EstimateCost(parsed.Ast, new RuleCostHints(types, parsed.Ast.Expr)), location);
        return refusal is null ? (env.Program(parsed.Ast), null) : (null, refusal);
    }

    private static ContractFinding? ProfileFinding(Celly.Ast.Expr expression, string location) =>
        RuleProfile.Violations(expression).FirstOrDefault() is { } reason ? Refusal(ContractFindingCodes.RuleFunctionNotAllowed, location, reason) : null;

    private static ContractFinding? ResultFinding(CelType result, string location) =>
        result.Kind is CelTypeKind.Bool or CelTypeKind.Dyn
            ? null
            : Refusal(ContractFindingCodes.RuleNotBoolean, location, $"A rule must be true or false, but this one is {result}.");

    private ContractFinding? CostFinding(CostEstimate cost, string location)
    {
        if (cost.IsUnbounded)
        {
            return Refusal(ContractFindingCodes.RuleCostUnbounded, location,
                "The schema does not bound this rule's cost. Give every array it iterates a maxItems, every map a maxProperties and every string it compares a maxLength.");
        }

        return cost.Max > (ulong)options.Value.MaxRuleCost
            ? Refusal(ContractFindingCodes.RuleCostExceeded, location, $"The rule may cost up to {cost.Max}, over the limit of {options.Value.MaxRuleCost}. Lower the schema's size limits, or simplify the rule.")
            : null;
    }

    private static ContractFinding Refusal(string code, string location, string message) =>
        new(code, ContractFindingSeverity.Error, location, SchemaViolations.Bound(message));

    private static string FirstIssue(IReadOnlyList<Celly.Common.CelIssue> issues) => string.Join("; ", issues.Take(1).Select(issue => issue.Message));

    /// <summary>The compiled rules of one contract.</summary>
    internal sealed class Compiled(CelSchemaTypes types, IReadOnlyList<(ContractRule Rule, CelProgram Program)> programs, int maxIterations) : ICompiledRules
    {
        /// <inheritdoc />
        public IReadOnlyList<ContractViolation> Evaluate(JsonElement payload, int maxViolations)
        {
            if (programs.Count == 0)
            {
                return [];
            }

            var bindings = new Dictionary<string, object?>(StringComparer.Ordinal) { [Self] = CelPayload.Convert(types, payload, string.Empty) };
            return programs
                .Select(item => RuleOutcome.Violation(item.Rule, item.Program.Eval(bindings, new EvalLimits { MaxIterations = maxIterations })))
                .OfType<ContractViolation>()
                .Take(maxViolations)
                .ToList();
        }
    }
}
