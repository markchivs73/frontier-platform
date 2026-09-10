using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-PA17 amendment: sample input becomes canonical dynamic-context JSON, and "nothing" is null.</summary>
public sealed class TestRunInputTests
{
    [Fact]
    public void ToDynamicContextJson_Object_IsCanonicalJson() =>
        Assert.Equal("{\"engagement_brief\":\"x\"}", TestRunInput.ToDynamicContextJson(new { engagement_brief = "x" }));

    [Fact]
    public void ToDynamicContextJson_EmptyObject_IsNull() => Assert.Null(TestRunInput.ToDynamicContextJson(new { }));

    [Fact]
    public void ToDynamicContextJson_EmptyDictionary_IsNull() => Assert.Null(TestRunInput.ToDynamicContextJson(new Dictionary<string, object>()));

    [Fact]
    public void ToDynamicContextJson_Null_IsNull() => Assert.Null(TestRunInput.ToDynamicContextJson(null));
}
