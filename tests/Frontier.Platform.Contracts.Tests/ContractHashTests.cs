using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D4: SHA-256 over the JCS form of the schema and rules.</summary>
public sealed class ContractHashTests
{
    private static readonly ContractRule[] Rules = [new() { Rule = "self.total >= 0.0", Message = "Never negative." }];

    [Fact]
    public void Compute_IsPrefixedLowerCaseHex() =>
        Assert.Matches("^sha256:[0-9a-f]{64}$", ContractHash.Compute(Json.Parse("{}"), []));

    [Fact]
    public void Compute_IgnoresKeyOrderAndWhitespace() =>
        Assert.Equal(
            ContractHash.Compute(Json.Parse("""{"type":"object","description":"x"}"""), Rules),
            ContractHash.Compute(Json.Parse("""{ "description" : "x", "type" : "object" }"""), Rules));

    [Fact]
    public void Compute_ChangesWithAnAnnotation() =>
        Assert.NotEqual(
            ContractHash.Compute(Json.Parse("""{"description":"x"}"""), Rules),
            ContractHash.Compute(Json.Parse("""{"description":"y"}"""), Rules));

    [Fact]
    public void Compute_ChangesWithARuleMessage_AndWithAFieldPath()
    {
        var schema = Json.Parse("{}");
        var baseline = ContractHash.Compute(schema, Rules);

        Assert.NotEqual(baseline, ContractHash.Compute(schema, [Rules[0] with { Message = "Other." }]));
        Assert.NotEqual(baseline, ContractHash.Compute(schema, [Rules[0] with { FieldPath = "/total" }]));
    }

    [Fact]
    public void Compute_KeepsRuleOrder()
    {
        var schema = Json.Parse("{}");
        ContractRule other = new() { Rule = "true", Message = "Always." };

        Assert.NotEqual(ContractHash.Compute(schema, [Rules[0], other]), ContractHash.Compute(schema, [other, Rules[0]]));
    }

    [Fact]
    public void HashInput_OmitsANullFieldPath_AndCarriesOneThatIsSet()
    {
        var schema = Json.Parse("{}");

        Assert.Equal("""{"rules":[{"rule":"true","message":"m"}],"schema":{}}""",
            System.Text.Encoding.UTF8.GetString(ContractHash.HashInput(schema, [new() { Rule = "true", Message = "m" }])));
        Assert.Equal("""{"rules":[{"rule":"true","message":"m","field_path":"/a"}],"schema":{}}""",
            System.Text.Encoding.UTF8.GetString(ContractHash.HashInput(schema, [new() { Rule = "true", Message = "m", FieldPath = "/a" }])));
    }

    [Fact]
    public void Compute_NullRules_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ContractHash.Compute(Json.Parse("{}"), null!));
}
