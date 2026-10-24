using System.Text;
using System.Text.Json;
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

        var canonical = CanonicalProfile.SerializeCanonical(section);
        Assert.Equal(Encoding.UTF8.GetString(canonical), payload);
        Assert.Equal(JsonCanonicalizer.Hash(canonical), hash);
    }

    [Fact]
    public void Build_FreeFormPayloadInTwoKeyOrders_HashesIdentically_AndKeepsEachPayloadAsProduced()
    {
        var first = Envelope("""{"name":"Ada","born":1815,"tags":{"b":2,"a":1}}""");
        var second = Envelope("""{"tags":{"a":1,"b":2},"born":1815,"name":"Ada"}""");

        var (firstPayload, firstHash) = OutputPayloadBuilder.Build(first, typeof(TypedPayload));
        var (secondPayload, secondHash) = OutputPayloadBuilder.Build(second, typeof(TypedPayload));

        Assert.Equal(firstHash, secondHash);
        Assert.NotEqual(firstPayload, secondPayload);
        Assert.Contains("\"name\":\"Ada\",\"born\"", firstPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FreeFormPayloadsThatDiffer_HashDifferently()
    {
        var (_, first) = OutputPayloadBuilder.Build(Envelope("""{"a":1}"""), typeof(TypedPayload));
        var (_, second) = OutputPayloadBuilder.Build(Envelope("""{"a":2}"""), typeof(TypedPayload));

        Assert.NotEqual(first, second);
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

    private static TypedPayload Envelope(string json) => new()
    {
        SchemaRef = "io.example/person/1.0",
        Payload = JsonDocument.Parse(json).RootElement.Clone(),
    };
}
