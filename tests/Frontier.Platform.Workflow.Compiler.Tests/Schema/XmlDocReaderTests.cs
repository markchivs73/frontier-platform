using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests.Schema;

/// <summary>Unit tests for <see cref="XmlDocReader"/> — doc-file parsing and summary lookup (doc 14 §7).</summary>
public sealed class XmlDocReaderTests
{
    [Fact]
    public void Normalize_CollapsesWhitespaceAndNewlines() =>
        Assert.Equal("Hello world from docs", XmlDocReader.Normalize("  Hello\n   world\t from   docs  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/no/such/file-xyz.xml")]
    public void Parse_MissingPath_ReturnsEmpty(string? path) =>
        Assert.Empty(XmlDocReader.Parse(path));

    [Fact]
    public void Parse_ValidFile_IndexesSummariesAndSkipsMembersWithoutOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xmldoc-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <doc><members>
              <member name="T:Foo"><summary>Hello  world</summary></member>
              <member name="P:Foo.Bar"><summary>Bar
                doc</summary></member>
              <member name="M:Foo.NoSummary()"></member>
              <member><summary>orphan without a name attribute</summary></member>
            </members></doc>
            """);
        try
        {
            var map = XmlDocReader.Parse(path);

            Assert.Equal("Hello world", map["T:Foo"]);
            Assert.Equal("Bar doc", map["P:Foo.Bar"]);
            Assert.DoesNotContain("M:Foo.NoSummary()", map.Keys);
            // Members lacking a name attribute or a summary are skipped: only the two valid ones remain.
            Assert.Equal(2, map.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeAndPropertySummary_BuildCorrectMemberKeys()
    {
        var reader = new XmlDocReader(new Dictionary<string, string>
        {
            ["T:System.String"] = "a string type",
            ["P:System.String.Length"] = "character count",
        });

        Assert.Equal("a string type", reader.TypeSummary(typeof(string)));
        Assert.Equal("character count", reader.PropertySummary(typeof(string), "Length"));
        Assert.Null(reader.PropertySummary(typeof(string), "Missing"));
        Assert.Null(reader.Lookup("T:Unknown"));
    }

    [Fact]
    public void ForAssembly_LoadsTheModelPackageDocFile()
    {
        // S13.12d: the model is a NuGet package now, and this assertion got sharper teeth as a
        // result. NuGet does not copy a package's XML docs to the output directory — the
        // consuming project must set CopyDocumentationFilesFromPackages. Without it the reader
        // finds nothing and every node/field description handed to the design agent is silently
        // empty: no exception, no missing type, just an emptier schema. This is the test that
        // notices.
        var reader = XmlDocReader.ForAssembly(typeof(WorkflowNode).Assembly);

        Assert.Contains("agent role", reader.PropertySummary(typeof(AgentTaskNode), "Role")!, StringComparison.Ordinal);
    }
}
