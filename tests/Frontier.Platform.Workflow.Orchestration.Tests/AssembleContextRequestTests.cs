using System.Text;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Tests for <see cref="AssembleContextRequest"/> validation (S3.3 ADR-CR1).</summary>
public sealed class AssembleContextRequestTests
{
    private static CachingMetadata Metadata() => new(
        ProviderId: "anthropic",
        ModelId: "claude-test",
        ModelVersion: null,
        MaxTokens: 4096,
        AssembledAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Validate_AllFieldsPresent_DoesNotThrow()
    {
        var request = new AssembleContextRequest(Metadata(), "baseline", "dynamic", "real-time");

        request.Validate();
    }

    [Fact]
    public void Validate_NullCachingMetadata_Throws()
    {
        var request = new AssembleContextRequest(null!, "baseline", "dynamic", "real-time");

        Assert.Throws<ArgumentNullException>(request.Validate);
    }

    [Fact]
    public void Validate_NullBaselineContent_Throws()
    {
        var request = new AssembleContextRequest(Metadata(), null!, "dynamic", "real-time");

        Assert.Throws<ArgumentNullException>(request.Validate);
    }

    [Fact]
    public void Validate_NullDynamicContent_Throws()
    {
        var request = new AssembleContextRequest(Metadata(), "baseline", null!, "real-time");

        Assert.Throws<ArgumentNullException>(request.Validate);
    }

    [Fact]
    public void Validate_NullRealTimeContent_Throws()
    {
        var request = new AssembleContextRequest(Metadata(), "baseline", "dynamic", null!);

        Assert.Throws<ArgumentNullException>(request.Validate);
    }

    [Fact]
    public void SerializeCanonical_BaselineProvenanceCited_WritesItLastInSnakeCase()
    {
        // S13.218: baseline_provenance is order 5 and its nested fields follow the canonical profile.
        var request = new AssembleContextRequest(Metadata(), "b", "d", "r")
        {
            BaselineProvenance = new BaselineTierProvenance("2026-q2", ["playbooks", "firm-standards"]),
        };

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(request));

        Assert.EndsWith(
            "\"real_time_content\":\"r\",\"baseline_provenance\":{\"baseline_version\":\"2026-q2\",\"components\":[\"playbooks\",\"firm-standards\"]}}",
            json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeCanonical_NoBaselineProvenance_OmitsTheField()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(
            new AssembleContextRequest(Metadata(), "b", "d", "r")));

        Assert.DoesNotContain("baseline_provenance", json, StringComparison.Ordinal);
        Assert.EndsWith("\"real_time_content\":\"r\"}", json, StringComparison.Ordinal);
    }
}
