using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S9.28 tests for <see cref="CanonicalOutputSchema"/>: the schema exporter emits the
/// boolean accept-anything schema <c>true</c> for any property whose type is handled by
/// a canonical custom converter, and the Anthropic API rejects boolean schemas — found
/// live when <c>ScoredMatch.Score</c> failed every <c>match_developer</c>
/// invocation with <c>invalid_request_error</c>.
/// </summary>
public sealed class CanonicalOutputSchemaTests
{
    [Fact]
    public void For_MatchDeveloperOutput_ScoreIsFixedPrecisionStringSchema()
    {
        var schema = SchemaOf(typeof(ScoredMatch));

        var score = schema.GetProperty("properties").GetProperty("score");

        Assert.Equal("string", score.GetProperty("type").GetString());
        Assert.Equal("^-?[0-9]+\\.[0-9]{4}$", score.GetProperty("pattern").GetString());
    }

    [Fact]
    public void For_SameType_ReturnsCachedInstance()
    {
        Assert.Same(CanonicalOutputSchema.For<ScoredMatch>(), CanonicalOutputSchema.For<ScoredMatch>());
    }

    [Fact]
    public void For_EveryVersionedContractInAbstractions_ProducesNoBooleanPropertySchemas()
    {
        // The regression guard the whole class exists for: every contract type the
        // reflection-based ContractTypeRegistry can name as an OutputContractType must
        // produce a schema Anthropic will accept. A new contract property whose type has
        // an unhandled custom converter fails here at PR time, not live with a 400.
        var contractTypes = typeof(WorkflowDefinition).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IVersionedContract).IsAssignableFrom(t)
                && t != typeof(TypedPayload));   // deliberately refused, not schematised — see the test below

        foreach (var type in contractTypes)
        {
            AssertNoBooleanPropertySchemas(SchemaOf(type), type.Name);
        }
    }

    [Fact]
    public void For_TypedPayload_ThrowsNotSupportedWithDesignReference()
    {
        // ADR-E2 deferral (c): TypedPayload's free-form payload/facts have no honest CLR-derived
        // schema — the real schema is its schema_ref, consumed by agent binding only under the
        // ADR-AG1 schema-validated variant (E4/E13). Until then the generator refuses the type
        // outright, so a definition wiring an AgentTaskNode to TypedPayload output fails fast and
        // legibly at invocation instead of live at the model API as an opaque 400.
        var exception = Assert.Throws<NotSupportedException>(CanonicalOutputSchema.For<TypedPayload>);

        Assert.Contains("ADR-AG1 schema-validated variant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_DecimalPrecisionAttribute_UsesDeclaredScale()
    {
        var schema = SchemaOf(typeof(MoneyFixture));

        var amount = schema.GetProperty("properties").GetProperty("amount");

        Assert.Equal("^-?[0-9]+\\.[0-9]{2}$", amount.GetProperty("pattern").GetString());
        Assert.Contains("\"1.25\"", amount.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void For_DateTimeProperty_DescribesIso8601String()
    {
        var schema = SchemaOf(typeof(TimestampFixture));

        var occurredAt = schema.GetProperty("properties").GetProperty("occurred_at");

        Assert.Equal("string", occurredAt.GetProperty("type").GetString());
        Assert.Contains("ISO-8601", occurredAt.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void For_SmartEnumProperty_EnumeratesWireNames()
    {
        var schema = SchemaOf(typeof(StatusFixture));

        var status = schema.GetProperty("properties").GetProperty("status");

        Assert.Equal("string", status.GetProperty("type").GetString());
        var names = status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(ExecutionStatus.List.Select(s => s.Name), names);
    }

    [Fact]
    public void For_UnhandledOpaqueConverter_LeavesBooleanSchemaForSweepToCatch()
    {
        var schema = SchemaOf(typeof(OpaqueFixture));

        var opaque = schema.GetProperty("properties").GetProperty("opaque");

        Assert.Equal(JsonValueKind.True, opaque.ValueKind);
    }

    [Fact]
    public void IsSmartEnum_NamePropertyWithoutFromName_IsFalse()
    {
        Assert.False(CanonicalOutputSchema.IsSmartEnum(typeof(NamedButNotSmartEnum)));
    }

    private static JsonElement SchemaOf(Type type)
    {
        var format = Assert.IsType<ChatResponseFormatJson>(CanonicalOutputSchema.For(type));
        Assert.NotNull(format.Schema);
        return format.Schema.Value;
    }

    /// <summary>Recursively asserts every member of every <c>properties</c> object is an object schema, never a boolean.</summary>
    private static void AssertNoBooleanPropertySchemas(JsonElement schema, string path)
    {
        if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var (item, index) in schema.EnumerateArray().Select((e, i) => (e, i)))
            {
                AssertNoBooleanPropertySchemas(item, $"{path}[{index}]");
            }

            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in schema.EnumerateObject())
        {
            if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in member.Value.EnumerateObject())
                {
                    Assert.True(
                        property.Value.ValueKind == JsonValueKind.Object,
                        $"{path}.{property.Name} is a boolean schema — its type's converter needs a CanonicalOutputSchema.TransformNode case.");
                }
            }

            AssertNoBooleanPropertySchemas(member.Value, $"{path}.{member.Name}");
        }
    }

    private sealed record MoneyFixture
    {
        [JsonPropertyName("amount")]
        [DecimalPrecision(2)]
        public required decimal Amount { get; init; }
    }

    private sealed record TimestampFixture
    {
        [JsonPropertyName("occurred_at")]
        public required DateTime OccurredAt { get; init; }
    }

    private sealed record StatusFixture
    {
        [JsonPropertyName("status")]
        public required ExecutionStatus Status { get; init; }
    }

    private sealed record OpaqueFixture
    {
        [JsonPropertyName("opaque")]
        [JsonConverter(typeof(OpaqueGuidConverter))]
        public required Guid Opaque { get; init; }
    }

    private sealed class OpaqueGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Guid.Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class NamedButNotSmartEnum
    {
        public string Name { get; } = "name";
    }
}
