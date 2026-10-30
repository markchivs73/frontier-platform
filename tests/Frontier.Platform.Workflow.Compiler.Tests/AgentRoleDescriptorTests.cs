using System.Text.Json;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Serialization;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-PA40: the designer's view of a role says what serves it.</summary>
public sealed class AgentRoleDescriptorTests
{
    [Fact]
    public void Backing_DefaultsToModel_TheStricterReading()
    {
        Assert.Equal(RoleBacking.Model, new AgentRoleDescriptor { RoleId = "fast", Description = "d" }.Backing);
    }

    [Fact]
    public void Backing_SerializesAsItsSnakeCaseName()
    {
        var json = JsonSerializer.Serialize(
            new AgentRoleDescriptor { RoleId = "ticket-triage", Description = "d", Backing = RoleBacking.Agent }, CanonicalProfile.Options);

        Assert.Equal("""{"role_id":"ticket-triage","description":"d","backing":"agent"}""", json);
    }
}
