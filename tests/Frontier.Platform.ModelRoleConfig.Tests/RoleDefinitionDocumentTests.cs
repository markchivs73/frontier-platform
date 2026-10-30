using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 byte-stability, round-trip and isolation tests for the stored role definition and its
/// pointer. A stored definition is the governed record of what a role is, so a change to these
/// golden files is a compatibility break, not a formatting tweak.
/// </summary>
public sealed class RoleDefinitionDocumentTests
{
    public static TheoryData<string> Phase1RoleIds => new("deep-reasoning", "fast", "structured-extraction", "embeddings");

    private static RoleDefinitionDocument ModelDocument => RoleDefinitionDocument.FromDomain(Migrated(Phase1Role("deep-reasoning")));

    private static RoleDefinitionDocument AgentDocument => RoleDefinitionDocument.FromDomain(AgentBacked());

    [Fact]
    public void ModelBackedDocument_IsByteStableAndMatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(ModelDocument);

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_definition_document_model.json");
    }

    [Fact]
    public void AgentBackedDocument_IsByteStableAndMatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(AgentDocument);

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_definition_document_agent.json");
    }

    [Fact]
    public void CurrentDocument_IsByteStableAndMatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(RoleDefinitionCurrentDocument.For(ModelDocument));

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_definition_current_document.json");
    }

    [Fact]
    public void EveryDocument_RoundTripsWithoutChangingBytes()
    {
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(ModelDocument);
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AgentDocument);
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(RoleDefinitionCurrentDocument.For(ModelDocument));
    }

    [Fact]
    public void AgentBackedDocument_WithoutRequirements_OmitsTheKey()
    {
        using var json = JsonDocument.Parse(CanonicalProfile.SerializeCanonical(AgentDocument));

        Assert.False(json.RootElement.TryGetProperty("requirements", out _));
        Assert.Equal("agent", json.RootElement.GetProperty("backing").GetString());
    }

    [Fact]
    public void EveryStoredKey_IsSnakeCase()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(ModelDocument));

        Assert.DoesNotMatch("\"[^\"]*[A-Z][^\"]*\":", json);
    }

    [Theory]
    [MemberData(nameof(Phase1RoleIds))]
    public void FromDomain_ThenToDomain_RestoresEveryField(string roleId)
    {
        var migrated = Migrated(Phase1Role(roleId));

        Assert.Equal(migrated, RoleDefinitionDocument.FromDomain(migrated).ToDomain());
    }

    [Fact]
    public void FromDomain_ThenToDomain_RestoresAnAgentBackedRole()
    {
        Assert.Equal(AgentBacked(), AgentDocument.ToDomain());
    }

    [Fact]
    public void FromDomain_SetsTheDeterministicIdsAndDocTypes()
    {
        var document = ModelDocument;
        var pointer = RoleDefinitionCurrentDocument.For(document);

        Assert.Equal("deep-reasoning:definition:v1", document.Id);
        Assert.Equal(RoleDefinitionDocument.DefinitionDocType, document.DocType);
        Assert.Equal(-1, document.Ttl);
        Assert.Equal("deep-reasoning:definition:current", pointer.Id);
        Assert.Equal(document.Id, pointer.CurrentRef);
        Assert.Equal(1, pointer.DefinitionVersion);
        Assert.Equal(RoleDefinitionCurrentDocument.CurrentDocType, pointer.DocType);
        Assert.Equal(-1, pointer.Ttl);
    }

    [Theory]
    [InlineData(nameof(RoleDefinition.DefinitionVersion))]
    [InlineData(nameof(RoleDefinition.ApprovedBy))]
    [InlineData(nameof(RoleDefinition.EffectiveFromUtc))]
    public void FromDomain_WithoutAnApprovalStamp_IsRefused(string stamp)
    {
        var migrated = Migrated(Phase1Role("fast"));
        var unstamped = stamp switch
        {
            nameof(RoleDefinition.DefinitionVersion) => migrated with { DefinitionVersion = null },
            nameof(RoleDefinition.ApprovedBy) => migrated with { ApprovedBy = null },
            _ => migrated with { EffectiveFromUtc = null },
        };

        var ex = Assert.Throws<ArgumentException>(() => RoleDefinitionDocument.FromDomain(unstamped));

        Assert.Contains(stamp, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDomain_StoredIllShapedDefinition_IsRefusedOnRead()
    {
        // A document edited outside this library still cannot reach the catalogue.
        var edited = AgentDocument with { OutputContractRef = null };

        Assert.Throws<ContractViolationException>(edited.ToDomain);
    }

    [Fact]
    public void Deserialize_UnknownBacking_IsRefused()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(ModelDocument)).Replace("\"backing\":\"model\"", "\"backing\":\"robot\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RoleDefinitionDocument>(json, CanonicalProfile.Options));
    }

    [Theory]
    [MemberData(nameof(Phase1RoleIds))]
    public void DefinitionDocuments_CanNeverBeSelectedAsMappingVersions(string roleId)
    {
        // CosmosMappingProposalStore lists mapping versions by STARTSWITH(id, '{roleId}:v') AND
        // IS_DEFINED(mapping_version). A definition document must fail both, so mapping history is
        // unchanged by the documents ADR-PA40 adds to the same partition.
        var version = RoleDefinitionDocument.FromDomain(Migrated(Phase1Role(roleId)));
        var pointer = RoleDefinitionCurrentDocument.For(version);
        var prefix = ModelRoleConfigDocumentId.VersionPrefix(roleId);

        foreach (var (id, bytes) in new[] { (version.Id, CanonicalProfile.SerializeCanonical(version)), (pointer.Id, CanonicalProfile.SerializeCanonical(pointer)) })
        {
            Assert.False(id.StartsWith(prefix, StringComparison.Ordinal));
            using var json = JsonDocument.Parse(bytes);
            Assert.False(json.RootElement.TryGetProperty("mapping_version", out _));
        }
    }
}
