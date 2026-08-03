namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="RoleMappingCurrentDocument"/>'s domain mapping (doc 08 §6).</summary>
public sealed class RoleMappingCurrentDocumentTests
{
    [Fact]
    public void FromDomain_PointsAtOwnVersion()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        var document = RoleMappingCurrentDocument.FromDomain(mapping);

        Assert.Equal("deep-reasoning:current", document.Id);
        Assert.Equal(mapping.RoleId, document.RoleId);
        Assert.Equal("deep-reasoning:v1", document.CurrentRef);
        Assert.Equal(mapping.MappingVersion, document.MappingVersion);
        Assert.Equal(-1, document.Ttl);
    }
}
