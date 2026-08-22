using System.Text.Json;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class TypedPayloadTests
{
    private const string ValidHash = "b5bb9d8014a0f9b1d61e21e796d78dccdf1352f23cd32812f4850b878ae4944c";

    [Fact]
    public void Validate_InlinePayload_DoesNotThrow()
    {
        var payload = new TypedPayload
        {
            SchemaRef = "schemas/classification-result/1.0",
            Payload = Json("""{"category":"product_upload"}"""),
        };

        payload.Validate();
    }

    [Fact]
    public void Validate_RefPayloadWithFacts_DoesNotThrow()
    {
        var payload = RefSample() with { Facts = Json("""{"row_count":4812}""") };

        payload.Validate();
    }

    [Fact]
    public void Validate_RefPayloadWithoutFacts_DoesNotThrow()
    {
        var payload = RefSample();

        payload.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankSchemaRef_Throws(string schemaRef)
    {
        var payload = RefSample() with { SchemaRef = schemaRef };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        Assert.Contains("schema_ref must not be empty.", exception.Violations);
    }

    [Fact]
    public void Validate_NeitherPayloadNorRef_Throws()
    {
        var payload = new TypedPayload { SchemaRef = "schemas/record-batch/1.0" };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        Assert.Contains("typed_payload must carry exactly one of payload or payload_ref.", exception.Violations);
    }

    [Fact]
    public void Validate_BothPayloadAndRef_Throws()
    {
        var payload = RefSample() with { Payload = Json("""{"inline":true}""") };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        Assert.Contains("typed_payload must carry exactly one of payload or payload_ref.", exception.Violations);
    }

    [Fact]
    public void Validate_FactsWithInlinePayload_Throws()
    {
        var payload = new TypedPayload
        {
            SchemaRef = "schemas/classification-result/1.0",
            Payload = Json("""{"category":"product_upload"}"""),
            Facts = Json("""{"row_count":1}"""),
        };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        Assert.Contains(
            "facts is only valid alongside payload_ref — an inline payload carries its own facts.",
            exception.Violations);
    }

    [Fact]
    public void Validate_InvalidNestedRef_CascadesPrefixedViolations()
    {
        var payload = RefSample() with
        {
            PayloadRef = new PayloadRef
            {
                StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/f.json?sig=abc"),
                ContentHash = ValidHash,
                ContentType = "application/json",
                SizeBytes = 1,
            },
        };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        Assert.Contains(
            "payload_ref: storage_uri must not carry a query string — persisted refs never contain access tokens (ADR-SEC5).",
            exception.Violations);
    }

    [Fact]
    public void Validate_EverythingInvalid_AggregatesViolations()
    {
        var payload = new TypedPayload
        {
            SchemaRef = "",
            Payload = Json("""{"inline":true}"""),
            PayloadRef = new PayloadRef
            {
                StorageUri = new Uri("relative/path", UriKind.Relative),
                ContentHash = "short",
                ContentType = " ",
                SizeBytes = 0,
            },
            Facts = Json("""{"row_count":1}"""),
        };

        var exception = Assert.Throws<ContractViolationException>(payload.Validate);

        // blank schema_ref + exclusivity + four cascaded payload_ref violations; facts is legal (ref present).
        Assert.Equal(6, exception.Violations.Count);
    }

    private static TypedPayload RefSample() => new()
    {
        SchemaRef = "schemas/record-batch/1.0",
        PayloadRef = new PayloadRef
        {
            StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/SUB-001/extract/rows.json"),
            ContentHash = ValidHash,
            ContentType = "application/json",
            SizeBytes = 7340032,
        },
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
