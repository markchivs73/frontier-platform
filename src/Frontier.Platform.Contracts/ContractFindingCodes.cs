namespace Frontier.Platform.Contracts;

/// <summary>
/// The registration finding codes (ADR-E27 D4, D5, D10). Stable: a registry screen or a test may
/// match on them.
/// </summary>
public static class ContractFindingCodes
{
    /// <summary>The ref is not <c>{namespace}/{name}/{major}.{minor}</c>.</summary>
    public const string RefMalformed = "contract.ref-malformed";

    /// <summary>The schema is not a JSON object.</summary>
    public const string SchemaNotObject = "contract.schema-not-object";

    /// <summary><c>$schema</c> names a dialect other than JSON Schema 2020-12.</summary>
    public const string DialectUnsupported = "contract.dialect-unsupported";

    /// <summary>A <c>$ref</c> or <c>$dynamicRef</c> points outside the contract.</summary>
    public const string RefNotLocal = "contract.ref-not-local";

    /// <summary>The schema does not compile.</summary>
    public const string SchemaInvalid = "contract.schema-invalid";

    /// <summary>The schema has no contract-level <c>description</c>.</summary>
    public const string DescriptionMissing = "contract.description-missing";

    /// <summary>A property has no <c>description</c>.</summary>
    public const string PropertyDescriptionMissing = "contract.property-description-missing";

    /// <summary>The schema has no <c>examples</c> entry.</summary>
    public const string ExamplesMissing = "contract.examples-missing";

    /// <summary>An <c>examples</c> entry does not satisfy the contract.</summary>
    public const string ExampleInvalid = "contract.example-invalid";

    /// <summary>An object schema states neither <c>additionalProperties</c> nor <c>unevaluatedProperties</c>.</summary>
    public const string ContentModelUnstated = "contract.content-model-unstated";

    /// <summary>A rule has no <c>message</c>.</summary>
    public const string RuleMessageMissing = "contract.rule-message-missing";

    /// <summary>A rule does not parse, or does not type-check against the schema's types.</summary>
    public const string RuleInvalid = "contract.rule-invalid";

    /// <summary>A rule uses something outside the CEL profile: <c>matches()</c> or a named time zone.</summary>
    public const string RuleFunctionNotAllowed = "contract.rule-function-not-allowed";

    /// <summary>A rule's result is not a boolean.</summary>
    public const string RuleNotBoolean = "contract.rule-not-boolean";

    /// <summary>The schema does not bound a rule's cost.</summary>
    public const string RuleCostUnbounded = "contract.rule-cost-unbounded";

    /// <summary>A rule's estimated cost is over <see cref="ContractsOptions.MaxRuleCost"/>.</summary>
    public const string RuleCostExceeded = "contract.rule-cost-exceeded";

    /// <summary>An <c>examples</c> entry fails a rule.</summary>
    public const string ExampleFailsRule = "contract.example-fails-rule";

    /// <summary>This exact version is already registered; a registered contract never changes.</summary>
    public const string VersionExists = "contract.version-exists";

    /// <summary>A later minor of the same major is already registered.</summary>
    public const string VersionNotNewer = "contract.version-not-newer";

    /// <summary>The schema differs structurally from an earlier minor of the same major.</summary>
    public const string MinorChangesSchema = "contract.minor-changes-schema";

    /// <summary>A rule is added or changed relative to an earlier minor of the same major.</summary>
    public const string MinorChangesRules = "contract.minor-changes-rules";
}
