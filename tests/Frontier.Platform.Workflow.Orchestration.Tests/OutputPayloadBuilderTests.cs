using System.Text;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="OutputPayloadBuilder"/>.</summary>
public sealed class OutputPayloadBuilderTests
{
    [Fact]
    public void Build_ValidContract_ReturnsCanonicalPayloadAndHash()
    {
        var section = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };

        var (payload, hash) = OutputPayloadBuilder.Build(section, typeof(SummaryArtifact));

        Assert.Equal(Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(section)), payload);
        Assert.Equal(CanonicalProfile.Hash(section), hash);
    }

    [Fact]
    public void Build_NullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OutputPayloadBuilder.Build(null!, typeof(SummaryArtifact)));
    }

    [Fact]
    public void Build_NullOutputType_ThrowsArgumentNullException()
    {
        var section = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };

        Assert.Throws<ArgumentNullException>(() => OutputPayloadBuilder.Build(section, null!));
    }
}
