namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// Tests for <see cref="ExternalEvent.Validate"/> (doc 16 §4). Its coverage exclusion ("Validate() not called at
/// runtime") hid four branches of validation logic from the gate; S13.193 removed it.
/// </summary>
public sealed class ExternalEventTests
{
    [Fact]
    public void Validate_CompleteEvent_DoesNotThrow()
    {
        var external = Valid();

        external.Validate();

        Assert.Equal(("zendesk", "ticket-created", "evt-1", "agent-7"), (external.SourceSystem, external.EventKind, external.ExternalEventId, external.Actor));
        Assert.Equal(new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), external.OccurredAtUtc);
        Assert.Equal("t-1", external.Payload["ticket_id"]);
    }

    [Theory]
    [InlineData("source", "SourceSystem")]
    [InlineData("kind", "EventKind")]
    [InlineData("id", "ExternalEventId")]
    public void Validate_BlankRequiredField_ThrowsNamingIt(string blank, string field)
    {
        var external = blank switch
        {
            "source" => Valid() with { SourceSystem = " " },
            "kind" => Valid() with { EventKind = "" },
            _ => Valid() with { ExternalEventId = " " },
        };

        var ex = Assert.Throws<InvalidOperationException>(external.Validate);

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NullPayload_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>((Valid() with { Payload = null! }).Validate);

        Assert.Contains("Payload", ex.Message, StringComparison.Ordinal);
    }

    private static ExternalEvent Valid() => new()
    {
        SourceSystem = "zendesk",
        EventKind = "ticket-created",
        ExternalEventId = "evt-1",
        Payload = new Dictionary<string, object?> { ["ticket_id"] = "t-1" },
        OccurredAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
        Actor = "agent-7",
    };
}
