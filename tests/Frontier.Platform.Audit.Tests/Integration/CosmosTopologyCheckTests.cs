using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>S5.2 tests for <see cref="CosmosTopologyCheck"/> (doc 12 §6, doc 05 §9).</summary>
public sealed class CosmosTopologyCheckTests
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void Name_ReturnsCosmosTopologyAudit()
    {
        using var client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, EmulatorKey);
        var check = new CosmosTopologyCheck(client, Options.Create(new CosmosOptions()));

        Assert.Equal("CosmosTopology:Audit", check.Name);
    }

    [Fact]
    public void Evaluate_AllContainersPresentWithExpectedPartitionKey_ReturnsPass()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["audit-telemetry-staging"] = "/execution_id",
            ["execution-snapshots"] = "/engagement_id",
            ["audit-records"] = "/engagement_id",
        };

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_MissingContainer_ReturnsFail()
    {
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["audit-telemetry-staging"] = null,
            ["execution-snapshots"] = "/engagement_id",
            ["audit-records"] = "/engagement_id",
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
            ["audit-telemetry-staging"] = "/executionId",
        };

        var result = CosmosTopologyCheck.Evaluate(CosmosTopologyCheck.ExpectedContainers, actual);

        Assert.False(result.Passed);
        Assert.Contains("expected '/execution_id'", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedContainers_MatchesAuditTelemetryStagingContainerName()
    {
        Assert.Equal("/execution_id", CosmosTopologyCheck.ExpectedContainers[CosmosAuditTelemetryStaging.ContainerName]);
    }

    [Fact]
    public void ExpectedContainers_MatchesAuditRecordsContainerName()
    {
        Assert.Equal("/engagement_id", CosmosTopologyCheck.ExpectedContainers[CosmosAuditRecordStore.ContainerName]);
    }
}
