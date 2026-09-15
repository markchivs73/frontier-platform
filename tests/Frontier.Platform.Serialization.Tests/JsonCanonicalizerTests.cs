using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// RFC 8785 conformance for <see cref="JsonCanonicalizer"/> (ADR-E2 decision 2, ADR-PA30). The
/// vectors are the RFC's own: §3.2.2 (the sample input and output), §3.2.3 (member sorting) and
/// Appendix B (IEEE-754 number serialization).
/// </summary>
public sealed class JsonCanonicalizerTests
{
    [Fact]
    public void Canonicalize_Rfc8785Section322Sample_ProducesTheRfcOutput()
    {
        const string input = """
            {
              "numbers": [333333333.33333329, 1E30, 4.50,
                          2e-3, 0.000000000000000000000000001],
              "string": "\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"\/",
              "literals": [null, true, false]
            }
            """;
        const string expected = "{\"literals\":[null,true,false],\"numbers\":[333333333.3333333,1e+30,4.5,0.002,1e-27],\"string\":\"€$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"}";

        Assert.Equal(expected, Canonical(input));
    }

    [Fact]
    public void Canonicalize_Rfc8785Section323Sample_SortsByUtf16CodeUnits()
    {
        const string input = """
            {
              "\u20ac": "Euro Sign",
              "\r": "Carriage Return",
              "\ufb33": "Hebrew Letter Dalet With Dagesh",
              "1": "One",
              "\ud83d\ude00": "Emoji: Grinning Face",
              "\u0080": "Control",
              "\u00f6": "Latin Small Letter O With Diaeresis"
            }
            """;

        using var document = JsonDocument.Parse(Canonical(input));
        string[] names = [.. document.RootElement.EnumerateObject().Select(property => property.Value.GetString()!)];

        Assert.Equal(
            ["Carriage Return", "One", "Control", "Latin Small Letter O With Diaeresis", "Euro Sign", "Emoji: Grinning Face", "Hebrew Letter Dalet With Dagesh"],
            names);
    }

    [Theory]
    [InlineData(0x0000000000000000UL, "0")]
    [InlineData(0x8000000000000000UL, "0")]
    [InlineData(0x0000000000000001UL, "5e-324")]
    [InlineData(0x8000000000000001UL, "-5e-324")]
    [InlineData(0x7fefffffffffffffUL, "1.7976931348623157e+308")]
    [InlineData(0xffefffffffffffffUL, "-1.7976931348623157e+308")]
    [InlineData(0x4340000000000000UL, "9007199254740992")]
    [InlineData(0xc340000000000000UL, "-9007199254740992")]
    [InlineData(0x4430000000000000UL, "295147905179352830000")]
    [InlineData(0x44b52d02c7e14af5UL, "9.999999999999997e+22")]
    [InlineData(0x44b52d02c7e14af6UL, "1e+23")]
    [InlineData(0x3eb0c6f7a0b5ed8dUL, "0.000001")]
    [InlineData(0x3eb0c6f7a0b5ed8cUL, "9.999999999999997e-7")]
    [InlineData(0x41b3de4355555553UL, "333333333.3333332")]
    [InlineData(0x44b52d02c7e14af7UL, "1.0000000000000001e+23")]
    [InlineData(0x444b1ae4d6e2ef4eUL, "999999999999999700000")]
    [InlineData(0x444b1ae4d6e2ef4fUL, "999999999999999900000")]
    [InlineData(0x444b1ae4d6e2ef50UL, "1e+21")]
    [InlineData(0x41b3de4355555554UL, "333333333.33333325")]
    [InlineData(0x41b3de4355555555UL, "333333333.3333333")]
    [InlineData(0x41b3de4355555556UL, "333333333.3333334")]
    [InlineData(0x41b3de4355555557UL, "333333333.33333343")]
    [InlineData(0xbecbf647612f3696UL, "-0.0000033333333333333333")]
    [InlineData(0x43143ff3c1cb0959UL, "1424953923781206.2")]
    public void FormatNumber_Rfc8785AppendixB_MatchesTheRfc(ulong bits, string expected) =>
        Assert.Equal(expected, JsonCanonicalizer.FormatNumber(BitConverter.UInt64BitsToDouble(bits)));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FormatNumber_NonFinite_Throws(double value) =>
        Assert.Throws<ArgumentException>(() => JsonCanonicalizer.FormatNumber(value));

    [Theory]
    [InlineData(123.0, "123")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1.5e-7, "1.5e-7")]
    [InlineData(12.25, "12.25")]
    public void FormatNumber_EachLayoutBranch_MatchesEcmaScript(double value, string expected) =>
        Assert.Equal(expected, JsonCanonicalizer.FormatNumber(value));

    [Fact]
    public void Canonicalize_EveryShortEscape_WritesTheJsonStringifyForm()
    {
        const string input = "[\"\\b\\f\\n\\r\\t\\u0001\\\"\\\\\"]";

        Assert.Equal("[\"\\b\\f\\n\\r\\t\\u0001\\\"\\\\\"]", Canonical(input));
    }

    [Fact]
    public void Canonicalize_WhitespaceAndMemberOrderDiffer_ProduceIdenticalBytes()
    {
        var first = JsonCanonicalizer.Canonicalize("{ \"b\" : [1, {\"y\":2,\"x\":1}], \"a\": \"z\" }"u8);
        var second = JsonCanonicalizer.Canonicalize("{\"a\":\"z\",\"b\":[1.0,{\"x\":1,\"y\":2.00}]}"u8);

        Assert.Equal(first, second);
        Assert.Equal("{\"a\":\"z\",\"b\":[1,{\"x\":1,\"y\":2}]}", Encoding.UTF8.GetString(first));
    }

    [Fact]
    public void Canonicalize_EmptyContainers_AreWrittenBare() =>
        Assert.Equal("{\"a\":[],\"b\":{}}", Canonical("{\"b\":{},\"a\":[]}"));

    [Fact]
    public void Canonicalize_UnderAnotherCulture_IsByteIdentical()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("[1.5,-2.25e-7]", Canonical("[1.5, -2.25e-7]"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Canonicalize_InvalidJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => JsonCanonicalizer.Canonicalize("{\"a\":"u8));

    private static string Canonical(string json) =>
        Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(json)));
}
