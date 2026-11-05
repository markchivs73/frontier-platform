using System.Net;
using System.Text;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S13.187: the fleet kill switch's stored shape in <c>platform-config</c> (doc 07 §5a, doc 02 §3).</summary>
public sealed class KillSwitchDocumentTests
{
    private static readonly DateTime At = new(2026, 11, 5, 9, 30, 0, 123, DateTimeKind.Utc);

    [Fact]
    public void Activated_IsTheFleetDocumentInTheKillSwitchPartition()
    {
        var document = KillSwitchDocument.Activated("cost runaway", At);

        Assert.Equal("kill-switch:fleet", document.Id);
        Assert.Equal("kill-switch", document.ConfigType);
        Assert.True(document.Active);
        Assert.Equal("cost runaway", document.Reason);
        Assert.Equal(At, document.ChangedAt);
    }

    [Fact]
    public void Resumed_ClearsTheReason()
    {
        var document = KillSwitchDocument.Resumed(At);

        Assert.False(document.Active);
        Assert.Null(document.Reason);
        Assert.Equal(At, document.ChangedAt);
    }

    [Fact]
    public void IsActive_OnlyAThrownDocumentDenies()
    {
        Assert.False(KillSwitchDocument.IsActive(null));
        Assert.False(KillSwitchDocument.IsActive(KillSwitchDocument.Resumed(At)));
        Assert.True(KillSwitchDocument.IsActive(KillSwitchDocument.Activated("x", At)));
    }

    [Fact]
    public void Serialize_UnderTheCanonicalProfile_WritesTheStoredShape()
    {
        var activated = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(KillSwitchDocument.Activated("cost runaway", At)));
        var resumed = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(KillSwitchDocument.Resumed(At)));

        Assert.Equal("""{"id":"kill-switch:fleet","config_type":"kill-switch","active":true,"reason":"cost runaway","changed_at":"2026-11-05T09:30:00.123Z"}""", activated);
        Assert.Equal("""{"id":"kill-switch:fleet","config_type":"kill-switch","active":false,"changed_at":"2026-11-05T09:30:00.123Z"}""", resumed);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 0, true)]
    [InlineData(HttpStatusCode.NotFound, 1003, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 0, false)]
    public void IsMissingItem_OnlyAnAbsentItemIsNoRecord(HttpStatusCode status, int subStatus, bool expected)
    {
        Assert.Equal(expected, CosmosItemNotFound.IsMissingItem(status, subStatus));
    }
}
