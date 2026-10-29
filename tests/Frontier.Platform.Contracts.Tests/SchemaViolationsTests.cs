namespace Frontier.Platform.Contracts.Tests;

public sealed class SchemaViolationsTests
{
    [Fact]
    public void From_KeepsOnlyTheDeepestFailures_AndNamesGenericOnesByKeyword()
    {
        SchemaFailure[] failures =
        [
            new("/a", "/properties/a", "The value was expected to match the subschema."),
            new("/a", "/properties/a/type", "The value was expected to be of type 'integer'"),
            new("/b", "/additionalProperties", "The value was expected to match the subschema."),
            new("/b", "/additionalProperties", ""),
            new("", "", "The value was expected to match the subschema."),
        ];

        var violations = SchemaViolations.From(failures, 10);

        Assert.Equal(
            [
                new ContractViolation("/a", "type", "The value was expected to be of type 'integer'"),
                new ContractViolation("/b", "additionalProperties", "The value failed 'additionalProperties'."),
            ],
            violations);
    }

    [Fact]
    public void From_ARootOnlyFailure_IsNamedSchema() =>
        Assert.Equal([new ContractViolation("", "schema", "The value failed 'schema'.")], SchemaViolations.From([new SchemaFailure("", "", "")], 10));

    [Fact]
    public void From_UnescapesTheKeyword_AndStopsAtTheLimit()
    {
        SchemaFailure[] failures = [new("/x", "/$defs/a~1b~0c", "m1"), new("/y", "/properties/y/type", "m2")];

        Assert.Equal([new ContractViolation("/x", "a/b~c", "m1")], SchemaViolations.From(failures, 1));
    }

    [Fact]
    public void Bound_CutsLongText()
    {
        var bounded = SchemaViolations.Bound(new string('k', 500));

        Assert.Equal(SchemaViolations.MaxTextLength, bounded.Length);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
        Assert.Equal("short", SchemaViolations.Bound("short"));
    }

    [Theory]
    [InlineData("/properties/a/type", "/properties/a", true)]
    [InlineData("/properties/a", "/properties/a", false)]
    [InlineData("/properties/ab", "/properties/a", false)]
    public void Extends_MeansStrictlyDeeper(string deeper, string location, bool expected) =>
        Assert.Equal(expected, SchemaViolations.Extends(deeper, location));
}
