namespace Frontier.Platform.ArchitectureTests;

/// <summary>S13.235 (mirrors the consumer's S13.212 tests): the literal lexer yields exactly the string literals' text, whatever the literal form.</summary>
public sealed class StringLiteralScannerTests
{
    /// <summary>Each literal form and lexer edge yields the expected texts, in source order.</summary>
    [Theory]
    [InlineData("var a = \"plain\";", "plain")]
    [InlineData("var a = \"say \\\"hi\\\"\";", "say \\\"hi\\\"")]
    [InlineData("var a = @\"C:\\dir \"\"q\"\"\";", "C:\\dir \"\"q\"\"")]
    [InlineData("var a = @\"line1\nline2\";", "line1\nline2")]
    [InlineData("var a = \"\"\"raw \"quoted\" text\"\"\";", "raw \"quoted\" text")]
    [InlineData("var a = $$\"\"\"{{x}} raw\"\"\";", "{{x}} raw")]
    [InlineData("var a = $\"x{{y}}z\";", "x{{y}}z")]
    [InlineData("var a = $\"a{b}c\";", "ac")]
    [InlineData("var a = $\"a{(f ? \"in\" : \"out\")}c\";", "in|out|ac")]
    [InlineData("var a = $\"a{new[] { 1 }.Length}c\";", "ac")]
    [InlineData("var a = $@\"v{x}\"\"\";", "v\"\"")]
    [InlineData("var q = '\"'; var s = \"after\";", "after")]
    [InlineData("var q = '\\''; var s = \"after\";", "after")]
    [InlineData("var d = a / b; var s = \"after\";", "after")]
    [InlineData("var x = 1; // \"not a literal\"\nvar s = \"after\";", "after")]
    [InlineData("/* \"not\" */ var s = \"after\";", "after")]
    [InlineData("var @class = 1; var s = \"after\";", "after")]
    [InlineData("var s = \"open\nvar t = \"next\";", "open|next")]
    [InlineData("var s = \"trailing\\", "trailing\\")]
    [InlineData("var s = \"\"\"never closed", "never closed")]
    [InlineData("var c = 'x", "")]
    [InlineData("/* never closed \"x\"", "")]
    [InlineData("// to the end \"x\"", "")]
    public void YieldsEachLiteralsText(string source, string expected) =>
        Assert.Equal(expected, string.Join('|', StringLiteralScanner.Scan(source).Select(l => l.Text)));

    /// <summary>A literal's line is where it opens, even when an earlier literal spans lines.</summary>
    [Fact]
    public void ReportsTheLineEachLiteralOpensOn() =>
        Assert.Equal(
            [1, 3],
            StringLiteralScanner.Scan("var a = @\"one\ntwo\";\nvar b = \"three\";").Select(l => l.Line));
}
