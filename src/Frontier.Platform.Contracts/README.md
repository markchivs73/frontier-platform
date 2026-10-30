# Frontier.Platform.Contracts

Contracts as data (ADR-PA38; the consumer's ADR-E27). A contract is a registry record: a JSON
Schema 2020-12 document plus CEL rules, hashed and immutable once registered. This package holds
the two decisions every contract goes through:

- **Registration:** may this contract be registered? (`IContractRegistrationCheck`)
- **Validation:** does this payload satisfy the contract it was pinned to? (`IContractValidator`)

Both are pure. The registry that stores contracts, and the engine that calls the validator at a
node's boundary, live elsewhere.

## Install

```bash
dotnet add package Frontier.Platform.Contracts
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierContracts(configuration); // binds and validates the "Contracts" section
```

Only the composition root should call it.

```csharp
var result = check.Check(new ContractRegistrationRequest
{
    ContractRef = "frontier/evidence-summary/1.0",
    Schema = schema,                       // a JsonElement
    Rules = [new() { Rule = "self.total >= 0.0", Message = "Never negative.", FieldPath = "/total" }],
    Provenance = ContractProvenance.Authored,
}, registeredVersionsOfThisId);

if (result.IsAccepted)
{
    store(result.Snapshot!);               // a ContractSnapshot, hashed
}

var validation = validator.Validate(snapshot, payload);
```

## What registration checks

| Check | Refuses | Finding codes |
|---|---|---|
| The ref is `{namespace}/{name}/{major}.{minor}` | always | `contract.ref-malformed` |
| The schema is a JSON object, in the 2020-12 dialect | always | `contract.schema-not-object`, `contract.dialect-unsupported` |
| Every `$ref` and `$dynamicRef` starts with `#` | always | `contract.ref-not-local` |
| The schema is valid against the 2020-12 meta-schema, and compiles | always | `contract.schema-invalid` |
| A contract `description`, a `description` on every property, at least one example, a `message` on every rule | authored and generated contracts; a warning for discovered ones | `contract.description-missing`, `contract.property-description-missing`, `contract.examples-missing`, `contract.rule-message-missing` |
| Every example satisfies the schema, and every rule | as above | `contract.example-invalid`, `contract.example-fails-rule` |
| Every rule parses and type-checks against the schema's types, is boolean, stays in the profile, and has a bounded cost under `MaxRuleCost` | always | `contract.rule-invalid`, `contract.rule-not-boolean`, `contract.rule-function-not-allowed`, `contract.rule-cost-unbounded`, `contract.rule-cost-exceeded` |
| Every object schema states `additionalProperties` or `unevaluatedProperties` | as above | `contract.content-model-unstated` |
| The version is new, and after every registered minor of its major | always | `contract.version-exists`, `contract.version-not-newer` |
| Within a major, a minor changes only annotations, removes rules, or rewords them | always | `contract.minor-changes-schema`, `contract.minor-changes-rules` |

A schema with a non-local reference is never compiled, so a refused contract cannot make the
engine read a file or fetch a URL. Compatibility is strict on purpose: with closed content models,
FULL_TRANSITIVE means each minor accepts exactly what the others accept, so any structural change
is a new major.

A generated contract starts from `CanonicalOutputSchema.For(type)` in
`Frontier.Platform.Workflow.Orchestration`, which gives each canonical-profile type its wire
schema. Its README lists what a generator adds before the export passes this check.

### Reading a registered contract back

`ContractLegibility` reads a registered `ContractSnapshot` for a person or an agent: its
contract-level `Description`, every property under `properties` at any depth with its description
(`Properties`, located as the lint locates them, such as `/schema/properties/total`), and its
legibility `Gaps`. `Gaps` is the registration lint run as warnings, so it is empty for an authored
or generated contract and lists what a discovered contract lacks. The chat designer's prompt uses
it (ADR-PA41); a registry screen can use it to show the same gaps.

## CEL rules

Rules are CEL (Celly 1.2.0, behind an internal port), evaluated with the payload bound as `self`:

- **`self` is typed from the schema.** An object with `properties` is a struct, so a rule naming a
  field the schema does not declare is refused at registration. Every JSON number is a `double`, as
  the CEL spec maps JSON: write `self.total + 1.0`, not `+ 1`. Unions and `oneOf` are `dyn`.
- **A missing field is an error, `has()` is the guard:** `!has(self.note) || size(self.note) < 200`.
- **The profile is the standard library** without `matches()` (use the schema's `pattern`) and
  without named time zones.
- **Cost is bounded by the schema.** A rule that iterates an array needs its `maxItems`, a map its
  `maxProperties`, and a string it compares its `maxLength`; otherwise the estimate is unbounded and
  the rule is refused. At run time each rule also has `MaxRuleIterations`, a count rather than a
  clock.
- **Maps are seen in key order,** so no result depends on the order a provider wrote them in.
- False, an evaluation error and an exhausted budget are all violations, reported with the rule's
  own `message` at its `field_path`. An evaluation error's text is never passed on.

## What validation guarantees

- **Schema first, then rules:** rules run only on a payload that satisfies the schema.
- **One engine, configured safely:** file and HTTP resolution off, `format` treated as an
  annotation, a regex timeout (`RegexMatchTimeoutMilliseconds`) that refuses the payload, and a
  nesting limit (`MaxPayloadDepth`) checked before the schema runs.
- **The snapshot is checked:** its hash is recomputed on every call. A snapshot whose schema or
  rules were changed after hashing is refused with `ContractViolationException`, never validated.
- **Violations are bounded and secret-free:** at most `MaxViolations`, each with a location, the
  keyword that failed and the engine's message. The message quotes the contract, never the
  payload; locations are capped at 200 characters.

A failed validation is a permanent contract violation. The caller raises it; nothing retries it.

## Shape questions for projections

`ContractShapes` answers what the compiler's `data.projection-compatible` needs to ask of the
contracts at either end of an edge projection: the schema of the value at an instance pointer
(local references inlined), whether that value is always present, whether two schemas accept the
same values (annotations aside), and which properties an object requires.

## Consumers get one extra build file

`Corvus.Text.Json`, which the JSON Schema engine depends on, ships Roslyn analyzers that flag
ordinary `System.Text.Json` code (CTJ001). This package's `buildTransitive` target removes them
from every consuming project, so a build that treats warnings as errors is unaffected. The smoke
consumer in `samples/` proves it against the packed package.
