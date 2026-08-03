namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="RoleMappingDocument"/>'s domain mapping (doc 08 §6).</summary>
public sealed class RoleMappingDocumentTests
{
    [Fact]
    public void FromDomain_SetsDeterministicId()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        var document = RoleMappingDocument.FromDomain(mapping);

        Assert.Equal("deep-reasoning:v1", document.Id);
        Assert.Equal(-1, document.Ttl);
    }

    [Fact]
    public void FromDomain_ThenToDomain_RoundTripsChain()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        var document = RoleMappingDocument.FromDomain(mapping);
        var roundTripped = document.ToDomain();

        Assert.Equal(mapping.Chain, roundTripped.Chain);
    }

    [Fact]
    public void FromDomain_MapsAllFields()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        var document = RoleMappingDocument.FromDomain(mapping);

        Assert.Equal(mapping.RoleId, document.RoleId);
        Assert.Equal(mapping.MappingVersion, document.MappingVersion);
        Assert.Equal(mapping.Ring, document.Ring);
        Assert.Equal(mapping.CanaryPercent, document.CanaryPercent);
        Assert.Equal(mapping.Chain, document.Chain.Select(entry => entry.ToDomain()));
        Assert.Equal(mapping.ChangeReason, document.ChangeReason);
        Assert.Equal(mapping.ApprovedBy, document.ApprovedBy);
        Assert.Equal(mapping.EffectiveFromUtc, document.EffectiveFromUtc);
        Assert.Equal(mapping.EvaluationEvidenceRef, document.EvaluationEvidenceRef);
        Assert.Null(document.PredecessorFleetVersion); // fleet mapping → null
    }

    [Fact]
    public void FromDomain_ThenToDomain_CanaryMapping_RoundTripsPredecessorFleetVersion()
    {
        var canary = new RoleMapping
        {
            RoleId = "deep-reasoning",
            MappingVersion = 2,
            Chain = [Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0]],
            Ring = RolloutRing.Canary,
            CanaryPercent = 10,
            ChangeReason = "canary test",
            ApprovedBy = "user:mark",
            EffectiveFromUtc = DateTime.UtcNow,
            PredecessorFleetVersion = 1,
        };

        var document = RoleMappingDocument.FromDomain(canary);
        var roundTripped = document.ToDomain();

        Assert.Equal(1, document.PredecessorFleetVersion);
        Assert.Equal(1, roundTripped.PredecessorFleetVersion);
    }
}
