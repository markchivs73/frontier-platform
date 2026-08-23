namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 construction test for <see cref="ComposedContext"/>.</summary>
public sealed class ComposedContextTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var context = new ComposedContext
        {
            BaselineContent = """{"a":1}""",
            DynamicContent = """{"b":2}""",
            RealTimeContent = "{}",
        };

        Assert.Equal("""{"a":1}""", context.BaselineContent);
        Assert.Equal("""{"b":2}""", context.DynamicContent);
        Assert.Equal("{}", context.RealTimeContent);
    }
}
