namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S9.25 tests for <see cref="McpToolRef"/>, migrated to the ADR-CD9 registry convention at S13.7b.</summary>
public sealed class McpToolRefTests
{
    [Fact]
    public void Parse_WellFormedReference_ReturnsServerAndTool()
    {
        var toolRef = McpToolRef.Parse("com.example.crm/tickets/get_new_ticket");

        Assert.Equal("com.example.crm/tickets", toolRef.Server);
        Assert.Equal("get_new_ticket", toolRef.Tool);
    }

    [Fact]
    public void Parse_SplitsAtLastSlash_ToolIsAlwaysTheLastSegment()
    {
        var toolRef = McpToolRef.Parse("com.example/crm/create_opportunity");

        Assert.Equal("com.example/crm", toolRef.Server);
        Assert.Equal("create_opportunity", toolRef.Tool);
    }

    [Fact]
    public void Parse_OldConnectorsConvention_ThrowsInvalidOperationException()
    {
        // The pre-S13.7b "connectors/{connector}.{tool}" form has one '/' and no dotted
        // namespace before it — a definition still carrying it must fail loudly, not
        // resolve to something surprising.
        Assert.Throws<InvalidOperationException>(() => McpToolRef.Parse("connectors/autotask-demo.get_new_ticket"));
    }

    [Fact]
    public void Parse_SingleSlash_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.Parse("com.example/get_new_ticket"));
    }

    [Fact]
    public void Parse_UndottedNamespace_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.Parse("frontier/autotask/get_new_ticket"));
    }

    [Fact]
    public void Parse_ThreeSlashes_ThrowsInvalidOperationException()
    {
        // A server name is exactly {namespace}/{name}; a third '/' would make the alias
        // non-round-trippable, so it is rejected outright.
        Assert.Throws<InvalidOperationException>(() => McpToolRef.Parse("com.example.crm/tickets/extra/get_new_ticket"));
    }

    [Fact]
    public void Parse_EmptyTool_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.Parse("com.example.crm/tickets/"));
    }

    [Fact]
    public void Parse_NullOrWhitespaceReference_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => McpToolRef.Parse(" "));
    }

    [Fact]
    public void ToWireReference_ReturnsCanonicalForm()
    {
        var toolRef = new McpToolRef("com.example.crm/tickets", "get_new_ticket");

        Assert.Equal("com.example.crm/tickets/get_new_ticket", toolRef.ToWireReference());
    }

    [Fact]
    public void ToModelSafeName_MapsNamespaceDotsAndSlashesToUnderscores()
    {
        var toolRef = new McpToolRef("com.example.crm/tickets", "get_new_ticket");

        Assert.Equal("mcp__com_example_crm__tickets__get_new_ticket", toolRef.ToModelSafeName());
    }

    [Fact]
    public void ParseModelSafeName_WellFormedAlias_ReturnsServerAndTool()
    {
        var toolRef = McpToolRef.ParseModelSafeName("mcp__com_example_crm__tickets__get_new_ticket");

        Assert.Equal("com.example.crm/tickets", toolRef.Server);
        Assert.Equal("get_new_ticket", toolRef.Tool);
    }

    [Fact]
    public void ParseModelSafeName_ToolNameContainingSeparator_KeepsToolVerbatim()
    {
        var toolRef = McpToolRef.ParseModelSafeName("mcp__com_example_scheduling__bookings__assign__resource");

        Assert.Equal("com.example.scheduling/bookings", toolRef.Server);
        Assert.Equal("assign__resource", toolRef.Tool);
    }

    [Fact]
    public void ParseModelSafeName_IsInverseOfToModelSafeName()
    {
        var original = new McpToolRef("com.example.crm/tickets", "get_new_ticket");

        var roundTripped = McpToolRef.ParseModelSafeName(original.ToModelSafeName());

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ParseModelSafeName_MissingPrefix_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("com_example_crm__tickets__get_new_ticket"));
    }

    [Fact]
    public void ParseModelSafeName_OldConnectorsAlias_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("connectors__autotask-demo__get_new_ticket"));
    }

    [Fact]
    public void ParseModelSafeName_MissingSecondSeparator_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("mcp__io_frontier_demo__autotask"));
    }

    [Fact]
    public void ParseModelSafeName_EmptyNamespace_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("mcp____autotask__get_new_ticket"));
    }

    [Fact]
    public void ParseModelSafeName_EmptyName_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("mcp__io_frontier_demo____get_new_ticket"));
    }

    [Fact]
    public void ParseModelSafeName_EmptyTool_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => McpToolRef.ParseModelSafeName("mcp__io_frontier_demo__autotask__"));
    }

    [Fact]
    public void ParseModelSafeName_NullOrWhitespaceAlias_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => McpToolRef.ParseModelSafeName(" "));
    }
}
