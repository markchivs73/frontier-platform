using System.Text.Json;

using Frontier.Platform.Serialization;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// ADR-PA41: the design agent's tool list is the descriptors serialized under the canonical
/// profile, so a tool's registered write flag reaches the agent as <c>writes</c>, and a tool the
/// registry holds no flag for keeps its existing bytes.
/// </summary>
public sealed class DesignerToolDescriptorWireTests
{
    private static readonly DesignerToolDescriptor Tool = new()
    {
        ToolRef = "crm/tickets/put",
        Server = "crm/tickets",
        Name = "put",
        Description = "Updates a ticket.",
    };

    [Fact]
    public void RegisteredWriteFlag_IsShownToTheAgent()
    {
        var json = JsonSerializer.Serialize(Tool with { Writes = true }, CanonicalProfile.Options);

        Assert.Equal("""{"tool_ref":"crm/tickets/put","server":"crm/tickets","name":"put","description":"Updates a ticket.","writes":true}""", json);
    }

    [Fact]
    public void NoRegisteredFlag_LeavesTheBytesUnchanged()
    {
        var json = JsonSerializer.Serialize(Tool, CanonicalProfile.Options);

        Assert.Equal("""{"tool_ref":"crm/tickets/put","server":"crm/tickets","name":"put","description":"Updates a ticket."}""", json);
    }
}
