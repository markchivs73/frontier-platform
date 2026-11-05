using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// S13.236 (K4, ADR-E2): a typed surface carries typed payloads. An <c>object</c> or
/// <c>JsonElement</c> property on a serialized platform contract is allowed only as an envelope with
/// a <c>schema_ref</c> beside it (<see cref="Envelopes"/>: each entry names the sibling string that
/// says which schema owns the shape, and a test checks the sibling is there), or as listed
/// <see cref="KnownDebt"/>, which may shrink but never grow. The contracts are every JSON-annotated
/// type in the platform libraries (<see cref="SerializedContracts.Types"/>). This mirrors the
/// consumer repository's <c>UntypedPayloadTests</c> (S13.213); this is the platform half.
/// </summary>
public sealed class UntypedPayloadTests
{
    /// <summary>The most <see cref="KnownDebt"/> entries there may be: lower it as debt is paid, never raise it.</summary>
    private const int KnownDebtCeiling = 1;

    /// <summary>
    /// Envelopes, keyed by <c>Namespace.Type.Property</c> so an entry exempts exactly one property,
    /// each naming its <c>schema_ref</c> sibling and the reason.
    /// </summary>
    private static readonly Dictionary<string, Envelope> Envelopes = new(StringComparer.Ordinal)
    {
        ["Frontier.Platform.Abstractions.ContractSnapshot.Schema"] = new("ContractRef", "a JSON Schema "
            + "2020-12 document: the schema is the payload, and the sibling contract_ref "
            + "({namespace}/{name}/{major}.{minor}) is its ADR-E2 reference, under which it is "
            + "registered, hashed and pinned (ADR-E27 D4)"),
        ["Frontier.Platform.Workflow.Model.TypedPayload.Payload"] = new("SchemaRef", "the ADR-E2 "
            + "carriage itself: inline capability content whose shape the sibling schema_ref names, "
            + "matched by the compiler on exact id and major version"),
        ["Frontier.Platform.Workflow.Model.ProjectionField.Literal"] = new("Target", "a fixed JSON "
            + "value for one consumer input field (ADR-E27 D14): the sibling target names that field, "
            + "and the compiler validates the literal against the field's schema in the edge's "
            + "consumer contract (DataProjectionCompatibleRule.LiteralFinding)"),
    };

    /// <summary>
    /// Untyped payloads that are K4 violations, not envelopes, kept only until their own task types
    /// them: listing them stops the rule failing on day one, and <see cref="KnownDebtCeiling"/>
    /// stops the list growing.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDebt = new(StringComparer.Ordinal)
    {
        ["Frontier.Platform.Workflow.Model.TypedPayload.Facts"] = "small inline values beside a "
            + "payload_ref, with no schema of their own: the sibling schema_ref names the staged "
            + "content's schema, and nothing validates facts against it. Typing them is a change to "
            + "the ADR-E2 carriage (schema version bump + migration), so it waits for its own task.",
    };

    /// <summary>No serialized contract carries an untyped payload that is neither an envelope nor known debt.</summary>
    [Fact]
    public void NoSerializedContractCarriesAnUntypedPayloadOutsideTheAllowlist()
    {
        var offenders = GovernedOffenders().Where(o => !IsAllowed(o)).ToList();

        Assert.True(offenders.Count == 0,
            "An object/JsonElement payload on a typed surface erodes K4: give it a typed contract, "
            + "or, if it is an envelope whose shape a named schema owns, list it in "
            + "Envelopes with its schema_ref sibling. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Anti-vacuity (S13.37): every allowlisted property still exists and is still untyped.</summary>
    [Fact]
    public void EveryAllowlistedPropertyIsStillAnUntypedPayload()
    {
        var offenders = GovernedOffenders().ToHashSet(StringComparer.Ordinal);
        var stale = Envelopes.Keys.Concat(KnownDebt.Keys).Where(key => !offenders.Contains(key)).ToList();

        Assert.True(stale.Count == 0, "Allowlisted but no longer an untyped payload — remove: " + string.Join(", ", stale));
    }

    /// <summary>K4's envelope-plus-<c>schema_ref</c>: every envelope has its named string sibling on the same contract.</summary>
    [Fact]
    public void EveryEnvelopeHasItsSchemaReferenceBesideIt()
    {
        var types = SerializedContracts.Types();
        var missing = Envelopes
            .Where(e => !types.Any(t => e.Key.StartsWith(t.FullName + ".", StringComparison.Ordinal)
                && UntypedPayloadRule.IsSchemaReference(t, e.Value.SchemaRef)))
            .Select(e => $"{e.Key} (expects {e.Value.SchemaRef})").ToList();

        Assert.True(missing.Count == 0, "Envelope without its schema_ref sibling: " + string.Join(", ", missing));
    }

    /// <summary>Every allowlist entry, envelope or debt, says why the payload may stay untyped.</summary>
    [Fact]
    public void EveryAllowlistEntryCarriesAReason()
    {
        Assert.All(Envelopes, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value.Reason), entry.Key));
        Assert.All(KnownDebt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    /// <summary>Known K4 debt may be paid down but not added to.</summary>
    [Fact]
    public void TheKnownDebtNeverGrows() =>
        Assert.True(KnownDebt.Count <= KnownDebtCeiling,
            $"{KnownDebt.Count} known-debt entries exceed the ceiling of {KnownDebtCeiling}: type the new payload instead.");

    /// <summary>Anti-vacuity: the discovered set is the real tree, including a known contract.</summary>
    [Fact]
    public void TheRuleReadsTheGovernedContracts()
    {
        var types = SerializedContracts.Types();

        Assert.Contains(types, t => t.FullName == "Frontier.Platform.Workflow.Model.TypedPayload");
        Assert.True(types.Count > 100, $"only {types.Count} serialized contracts were discovered");
    }

    /// <summary>A planted untyped property, in each shape the rule claims, is caught.</summary>
    [Theory]
    [InlineData(nameof(PlantedContract.Bare))]
    [InlineData(nameof(PlantedContract.Element))]
    [InlineData(nameof(PlantedContract.NullableElement))]
    [InlineData(nameof(PlantedContract.Document))]
    [InlineData(nameof(PlantedContract.Node))]
    [InlineData(nameof(PlantedContract.ObjectNode))]
    [InlineData(nameof(PlantedContract.Bag))]
    [InlineData(nameof(PlantedContract.Elements))]
    [InlineData(nameof(PlantedContract.Objects))]
    [InlineData(nameof(PlantedContract.IgnoredWhenNull))]
    public void APlantedUntypedPayloadIsCaught(string property) =>
        Assert.Contains($"{typeof(PlantedContract).FullName}.{property}", UntypedPayloadRule.Offenders(typeof(PlantedContract)));

    /// <summary>
    /// Typed properties and an always-ignored one are not caught (a conditionally ignored one is
    /// still written when set, so the theory above catches it).
    /// </summary>
    [Theory]
    [InlineData(nameof(PlantedContract.Name))]
    [InlineData(nameof(PlantedContract.Tags))]
    [InlineData(nameof(PlantedContract.Ignored))]
    public void ATypedOrAlwaysIgnoredPropertyIsNotCaught(string property) =>
        Assert.DoesNotContain($"{typeof(PlantedContract).FullName}.{property}", UntypedPayloadRule.Offenders(typeof(PlantedContract)));

    /// <summary>Only JSON-annotated types are serialized contracts, so in-memory shapes stay out of scope.</summary>
    [Fact]
    public void OnlyJsonAnnotatedTypesAreSerializedContracts()
    {
        Assert.True(SerializedContracts.IsSerializedContract(typeof(PlantedContract)));
        Assert.False(SerializedContracts.IsSerializedContract(typeof(InMemoryShape)));
    }

    /// <summary>The schema reference check refuses a missing, untyped or unannotated sibling.</summary>
    [Theory]
    [InlineData(nameof(PlantedContract.Name), true)]
    [InlineData(nameof(PlantedContract.Bare), false)]
    [InlineData(nameof(PlantedContract.Unannotated), false)]
    [InlineData("Missing", false)]
    public void ASchemaReferenceIsADeclaredJsonString(string property, bool expected) =>
        Assert.Equal(expected, UntypedPayloadRule.IsSchemaReference(typeof(PlantedContract), property));

    private static bool IsAllowed(string property) =>
        Envelopes.ContainsKey(property) || KnownDebt.ContainsKey(property);

    private static IEnumerable<string> GovernedOffenders() =>
        SerializedContracts.Types().SelectMany(UntypedPayloadRule.Offenders).Order(StringComparer.Ordinal);

    /// <summary>A planted violation: one property per untyped shape, beside typed and ignored ones.</summary>
    private sealed class PlantedContract
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
        [JsonPropertyName("bare")] public object? Bare { get; init; }
        [JsonPropertyName("element")] public JsonElement Element { get; init; }
        [JsonPropertyName("nullable_element")] public JsonElement? NullableElement { get; init; }
        [JsonPropertyName("document")] public JsonDocument? Document { get; init; }
        [JsonPropertyName("node")] public JsonNode? Node { get; init; }
        [JsonPropertyName("object_node")] public JsonObject? ObjectNode { get; init; }
        [JsonPropertyName("bag")] public IReadOnlyDictionary<string, object?>? Bag { get; init; }
        [JsonPropertyName("elements")] public List<JsonElement>? Elements { get; init; }
        [JsonPropertyName("objects")] public object[]? Objects { get; init; }
        [JsonIgnore] public object? Ignored { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public object? IgnoredWhenNull { get; init; }
        public string Unannotated { get; init; } = "";
    }

    /// <summary>An allowlisted envelope: the sibling property naming its schema, and why it is untyped.</summary>
    private sealed record Envelope(string SchemaRef, string Reason);

    /// <summary>No JSON annotations: an in-memory construction contract.</summary>
    private sealed class InMemoryShape
    {
        public object? Payload { get; init; }
    }
}
