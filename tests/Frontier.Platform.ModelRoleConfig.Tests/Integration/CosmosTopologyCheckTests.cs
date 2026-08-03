using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>S4.7b tests for <see cref="CosmosTopologyCheck"/> (doc 12 §6, doc 08 §6).</summary>
public sealed class CosmosTopologyCheckTests
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void Name_ReturnsCosmosTopologyModelRoleConfig()
    {
        using var client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, EmulatorKey);
        var check = new CosmosTopologyCheck(client, Options.Create(new CosmosOptions()));

        Assert.Equal("CosmosTopology:ModelRoleConfig", check.Name);
    }

    [Fact]
    public void Evaluate_AllContainersPresentWithExpectedPartitionKey_ReturnsPass()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["model-role-config"] = "/role_id",
        };

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_MissingContainer_ReturnsFail()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["model-role-config"] = null,
        };

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.False(result.Passed);
        Assert.Contains("not found", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ContainerAbsentFromActual_ReturnsFail()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal);

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.False(result.Passed);
        Assert.Contains("not found", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WrongPartitionKeyPath_ReturnsFail()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["model-role-config"] = "/engagementId",
        };

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.False(result.Passed);
        Assert.Contains("expected '/role_id'", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedContainers_MatchesRoleRegistryContainerName()
    {
        Assert.Equal("/role_id", CosmosTopologyCheck.ExpectedContainers[CosmosRoleRegistry.ContainerName]);
    }
}
