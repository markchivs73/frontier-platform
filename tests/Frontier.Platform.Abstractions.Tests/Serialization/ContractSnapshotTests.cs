using System.Text.Json;

using Frontier.TestSupport;

namespace Frontier.Platform.Abstractions.Tests.Serialization;

/// <summary>ADR-E27 D4: the pinned contract's wire shape and its format checks.</summary>
public sealed class ContractSnapshotTests
{
    [Fact]
    public void Snapshot_IsByteStable_MatchesItsGoldenFile_AndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Sample(), "contract_snapshot.json");

    [Fact]
    public void Validate_AWellFormedSnapshot_DoesNotThrow() => Sample().Validate();

    [Fact]
    public void Validate_ReportsEveryMalformedField()
    {
        var snapshot = Sample() with
        {
            ContractRef = " ",
            Schema = JsonDocument.Parse("[]").RootElement,
            Rules = null!,
            Hash = "md5:abc",
        };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Equal(
            ["contract_ref is required", "schema must be a JSON object", "rules must not be null", "hash must be a sha256: content hash"],
            exception.Violations);
    }

    [Fact]
    public void Validate_AnEmptyHash_Throws()
    {
        var exception = Assert.Throws<ContractViolationException>((Sample() with { Hash = string.Empty }).Validate);

        Assert.Equal(["hash must be a sha256: content hash"], exception.Violations);
    }

    private static ContractSnapshot Sample() => new()
    {
        ContractRef = "frontier/evidence-summary/1.0",
        Schema = JsonDocument.Parse("""{"type":"object","description":"A summary.","additionalProperties":false}""").RootElement,
        Rules =
        [
            new ContractRule { Rule = "size(self) > 0", Message = "It must not be empty.", FieldPath = "/items" },
            new ContractRule { Rule = "true", Message = "Always." },
        ],
        Hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    };
}
