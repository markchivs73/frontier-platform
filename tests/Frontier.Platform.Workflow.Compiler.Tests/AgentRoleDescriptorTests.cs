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

    [Fact]
    public void Stakes_SerializesAfterTheDescription_AndIsOmittedWhenNull()
    {
        var withStakes = JsonSerializer.Serialize(
            new AgentRoleDescriptor { RoleId = "deep-reasoning", Description = "d", Stakes = StakesLevel.Material }, CanonicalProfile.Options);
        var without = JsonSerializer.Serialize(
            new AgentRoleDescriptor { RoleId = "deep-reasoning", Description = "d" }, CanonicalProfile.Options);

        Assert.Equal("""{"role_id":"deep-reasoning","description":"d","stakes":"material","backing":"model"}""", withStakes);
        Assert.Equal("""{"role_id":"deep-reasoning","description":"d","backing":"model"}""", without);
    }
}
