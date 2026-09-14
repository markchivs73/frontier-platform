using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;

namespace Frontier.Platform.Audit.Tests;

/// <summary>ADR-PA28: the provenance enum and the optional <c>provenance</c>/<c>note</c> fields on <see cref="ToolCall"/>.</summary>
public sealed class ToolCallProvenanceTests
{
    [Fact]
    public void List_Always_ReturnsBothValuesInDeclarationOrder()
    {
        Assert.Equal([ToolCallProvenance.Observed, ToolCallProvenance.SelfReported], ToolCallProvenance.List);
    }

    [Theory]
    [InlineData("observed")]
    [InlineData("self_reported")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ToolCallProvenance.FromName(name).Name);
    }

    [Fact]
    public void FromName_UnknownName_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolCallProvenance.FromName("claimed"));
    }

    [Fact]
    public void SelfReportedToolCall_SerializesAsSnakeCaseStringAndRoundTrips()
    {
        var call = AuditContractSamples.ToolCall() with
        {
            Provenance = ToolCallProvenance.SelfReported,
            Note = "Computed received_sha256 over the request body.",
        };

        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(call);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.Equal(
            "{\"name\":\"connectors/crm.create_opportunity\",\"invoked_at_utc\":\"2026-01-01T00:22:00.000Z\"," +
            "\"provenance\":\"self_reported\",\"note\":\"Computed received_sha256 over the request body.\"}",
            json);
        Assert.Equal(call, JsonSerializer.Deserialize<ToolCall>(bytes, CanonicalProfile.Options));
    }

    [Fact]
    public void ObservedToolCall_OmitsProvenanceAndNote_BytesUnchanged()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(AuditContractSamples.ToolCall()));

        Assert.Equal("{\"name\":\"connectors/crm.create_opportunity\",\"invoked_at_utc\":\"2026-01-01T00:22:00.000Z\"}", json);
    }

    [Fact]
    public void PreFieldRecord_Deserializes_WithNullProvenanceAndNote()
    {
        const string preField = "{\"name\":\"connectors/crm.create_opportunity\",\"invoked_at_utc\":\"2026-01-01T00:22:00.000Z\"}";

        var call = JsonSerializer.Deserialize<ToolCall>(preField, CanonicalProfile.Options);

        Assert.NotNull(call);
        Assert.Null(call.Provenance);
        Assert.Null(call.Note);
    }
}
