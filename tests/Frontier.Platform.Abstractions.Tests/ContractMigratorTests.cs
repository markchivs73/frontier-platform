using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class ContractMigratorTests
{
    [Fact]
    public void Rehydrate_NoAdaptersProvided_DeserializesDirectly()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            {"schema_version":"1.0","source_system":"autotask","event_kind":"schedule_changed","dedupe_id":"abc123","payload":"{}"}
            """);

        var result = ContractMigrator.Rehydrate<SampleSignal>(bytes, CanonicalProfile.Options);

        Assert.Equal("1.0", result.SchemaVersion);
        Assert.Equal("autotask", result.SourceSystem);
    }

    [Fact]
    public void Rehydrate_AdaptersProvidedButNoMatchingVersion_DeserializesDirectly()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            {"schema_version":"1.0","source_system":"autotask","event_kind":"schedule_changed","dedupe_id":"abc123","payload":"{}"}
            """);
        var adapters = new Dictionary<string, Func<JsonElement, JsonSerializerOptions, SampleSignal>>
        {
            ["0.9"] = (_, _) => throw new InvalidOperationException("Adapter should not run for an unmatched version."),
        };

        var result = ContractMigrator.Rehydrate(bytes, CanonicalProfile.Options, adapters);

        Assert.Equal("autotask", result.SourceSystem);
        Assert.Equal("{}", result.Payload);
    }

    [Fact]
    public void Rehydrate_AdaptersProvidedWithMatchingVersion_RoutesThroughAdapter()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            {"schema_version":"0.9","source_system":"autotask","event_kind":"schedule_changed","dedupe_id":"abc123","raw_payload":"{}"}
            """);
        var adapters = new Dictionary<string, Func<JsonElement, JsonSerializerOptions, SampleSignal>>
        {
            ["0.9"] = (element, _) => new SampleSignal
            {
                SchemaVersion = "1.0",
                SourceSystem = element.GetProperty("source_system").GetString()!,
                EventKind = element.GetProperty("event_kind").GetString()!,
                DedupeId = element.GetProperty("dedupe_id").GetString()!,
                Payload = element.GetProperty("raw_payload").GetString()!,
            },
        };

        var result = ContractMigrator.Rehydrate(bytes, CanonicalProfile.Options, adapters);

        Assert.Equal("1.0", result.SchemaVersion);
        Assert.Equal("{}", result.Payload);
    }

    [Fact]
    public void Rehydrate_MissingSchemaVersionProperty_DefaultsToOnePointZero()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            {"source_system":"autotask","event_kind":"schedule_changed","dedupe_id":"abc123","payload":"{}"}
            """);

        var result = ContractMigrator.Rehydrate<SampleSignal>(bytes, CanonicalProfile.Options);

        Assert.Equal("1.0", result.SchemaVersion);
    }

    [Fact]
    public void Rehydrate_SchemaVersionPropertyIsJsonNull_DefaultsToOnePointZero()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            {"schema_version":null,"source_system":"autotask","event_kind":"schedule_changed","dedupe_id":"abc123","payload":"{}"}
            """);
        var adapters = new Dictionary<string, Func<JsonElement, JsonSerializerOptions, SampleSignal>>
        {
            ["1.0"] = (element, _) => new SampleSignal
            {
                SchemaVersion = "1.0",
                SourceSystem = element.GetProperty("source_system").GetString()!,
                EventKind = element.GetProperty("event_kind").GetString()!,
                DedupeId = element.GetProperty("dedupe_id").GetString()!,
                Payload = "from-adapter",
            },
        };

        var result = ContractMigrator.Rehydrate(bytes, CanonicalProfile.Options, adapters);

        Assert.Equal("from-adapter", result.Payload);
    }
}
