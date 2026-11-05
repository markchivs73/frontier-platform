using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>S13.185: <see cref="WireShapeReader"/> reads the shape the canonical profile reads and writes.</summary>
public sealed class WireShapeReaderTests
{
    [Fact]
    public void Read_FlattensMembersArraysMapsAndRequiredness()
    {
        var shape = WireShapeReader.Read(typeof(SampleContract));

        Assert.Equal(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["$"] = "object",
                ["$.count"] = "integer",
                ["$.id"] = "string (required)",
                ["$.labels"] = "map",
                ["$.labels{}"] = "decimal(scale=4)",
                ["$.price"] = "decimal(scale=2)",
                ["$.status"] = "enum(0|1)",
                ["$.tags"] = "array (required)",
                ["$.tags[]"] = "string",
            },
            shape);
    }

    [Fact]
    public void Read_ExpandsPolymorphicVariantsAndStopsAtRecursion()
    {
        var shape = WireShapeReader.Read(typeof(SampleNode));

        Assert.Equal("polymorphic(kind)", shape["$"]);
        Assert.Equal("string (required)", shape["$<leaf>.name"]);
        Assert.Equal("recursive", shape["$<branch>.children[]"]);
        Assert.DoesNotContain("$.name", shape.Keys);
    }

    [Theory]
    [InlineData(typeof(Guid), "string")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(short), "integer")]
    [InlineData(typeof(byte), "integer")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(DateTime), "date_time")]
    [InlineData(typeof(DateTimeOffset), "date_time")]
    [InlineData(typeof(TimeSpan), "duration")]
    [InlineData(typeof(object), "any")]
    [InlineData(typeof(JsonElement), "any")]
    [InlineData(typeof(decimal), "decimal(scale=4)")]
    [InlineData(typeof(SampleStatus), "enum(0|1)")]
    [InlineData(typeof(Abstractions.ExecutionStatus), "enum(cancelled|completed|failed|paused_at_gate|paused_on_failure|running)")]
    [InlineData(typeof(ListlessSmartEnum), "string")]
    [InlineData(typeof(Abstractions.EngagementId), "string")]
    [InlineData(typeof(Uri), "scalar:Uri")]
    public void ScalarToken_NamesTheWireKind(Type type, string token)
    {
        ArgumentNullException.ThrowIfNull(type);
        Assert.Equal(token, WireShapeReader.ScalarToken(type, CanonicalProfile.Options.GetConverter(type)));
    }

    [Fact]
    public void EnumValues_OfANonEnum_IsNull() =>
        Assert.Null(WireShapeReader.EnumValues(typeof(string)));

    [Fact]
    public void EnumValuesOf_ReadsOnlyAnEnumToken()
    {
        Assert.Equal(["a", "b"], WireShapeReader.EnumValuesOf("enum(a|b)"));
        Assert.Null(WireShapeReader.EnumValuesOf("string"));
    }

    [Fact]
    public void Read_ANullableScalar_ReadsItsUnderlyingType() =>
        Assert.Equal("integer", WireShapeReader.Read(typeof(int?))["$"]);

    [Fact]
    public void Markers_RoundTrip()
    {
        Assert.Equal("string (required)", WireShapeReader.Mark("string", required: true));
        Assert.Equal("string", WireShapeReader.Mark("string", required: false));
        Assert.True(WireShapeReader.IsRequired("string (required)"));
        Assert.Equal("string", WireShapeReader.Bare("string (required)"));
        Assert.Equal("string", WireShapeReader.Bare("string"));
    }

    /// <summary>A plain enum: the profile registers no string-enum converter, so it writes the number.</summary>
    internal enum SampleStatus
    {
        /// <summary>The first value.</summary>
        Open,

        /// <summary>The second value.</summary>
        Closed,
    }

    /// <summary>Shaped like a smart enum for the profile's factory, but with no static <c>List</c> to read values from.</summary>
    internal sealed class ListlessSmartEnum
    {
        private ListlessSmartEnum(string name) => Name = name;

        /// <summary>The wire name.</summary>
        public string Name { get; }

        /// <summary>Resolves a wire name.</summary>
        public static ListlessSmartEnum FromName(string name) => new(name);
    }

    /// <summary>A contract exercising each member kind.</summary>
    internal sealed record SampleContract
    {
        /// <summary>A required string.</summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        /// <summary>A required array.</summary>
        [JsonPropertyOrder(1)]
        [JsonPropertyName("tags")]
        public required IReadOnlyList<string> Tags { get; init; }

        /// <summary>An optional nullable number.</summary>
        [JsonPropertyOrder(2)]
        [JsonPropertyName("count")]
        public int? Count { get; init; }

        /// <summary>An optional map.</summary>
        [JsonPropertyOrder(3)]
        [JsonPropertyName("labels")]
        public IReadOnlyDictionary<string, decimal>? Labels { get; init; }

        /// <summary>An optional enum.</summary>
        [JsonPropertyOrder(4)]
        [JsonPropertyName("status")]
        public SampleStatus? Status { get; init; }

        /// <summary>An optional nullable money amount with its own scale.</summary>
        [JsonPropertyOrder(5)]
        [JsonPropertyName("price")]
        [DecimalPrecision(2)]
        public decimal? Price { get; init; }

        /// <summary>Never on the wire.</summary>
        [JsonIgnore]
        public string Ignored => Id;
    }

    /// <summary>A polymorphic, self-referencing base.</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(SampleLeaf), "leaf")]
    [JsonDerivedType(typeof(SampleBranch), "branch")]
    internal abstract record SampleNode
    {
        /// <summary>Shared by every variant.</summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("name")]
        public required string Name { get; init; }
    }

    /// <summary>A variant with no children.</summary>
    internal sealed record SampleLeaf : SampleNode;

    /// <summary>A variant that nests the base again.</summary>
    internal sealed record SampleBranch : SampleNode
    {
        /// <summary>The recursion.</summary>
        [JsonPropertyOrder(1)]
        [JsonPropertyName("children")]
        public IReadOnlyList<SampleNode>? Children { get; init; }
    }
}
