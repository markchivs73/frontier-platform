using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="RoleBacking"/>'s canonical wire values.</summary>
public sealed class RoleBackingTests
{
    [Fact]
    public void List_Always_ReturnsModelThenAgent()
    {
        Assert.Equal([RoleBacking.Model, RoleBacking.Agent], RoleBacking.List);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("agent")]
    public void Serialize_KnownValue_IsItsCanonicalString(string name)
    {
        var json = CanonicalProfile.SerializeCanonical(RoleBacking.FromName(name));

        Assert.Equal($"\"{name}\"", System.Text.Encoding.UTF8.GetString(json));
    }

    [Fact]
    public void Deserialize_UnknownValue_IsRefused()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RoleBacking>("\"robot\"", CanonicalProfile.Options));
    }

    [Fact]
    public void RoleDefinition_WithoutABacking_DefaultsToModel()
    {
        var role = new RoleDefinition { RoleId = "fast", Description = "d", Stakes = StakesLevel.Standard };

        Assert.Equal(RoleBacking.Model, role.Backing);
    }
}
