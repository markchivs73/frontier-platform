using System.Reflection;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.65: <see cref="AuditRecordHasher.ToSignedShape"/> copies <see cref="AuditRecord"/> into
/// <see cref="SignedAuditRecord"/> one named field at a time, so a property added to the unsigned
/// record and not to the signed one is silently dropped before hashing, signing and persistence —
/// which is exactly what happened to <c>sandbox</c> (S9.38e) and the provenance stamp (#27). These
/// tests fail the moment that can happen again.
/// </summary>
public sealed class SignedShapeParityTests
{
    private static IEnumerable<PropertyInfo> ContentProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead);

    [Fact]
    public void EveryAuditRecordProperty_ExistsOnSignedAuditRecord()
    {
        var signed = ContentProperties(typeof(SignedAuditRecord)).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var missing = ContentProperties(typeof(AuditRecord)).Select(p => p.Name).Where(n => !signed.Contains(n)).ToList();

        Assert.True(missing.Count == 0, $"AuditRecord properties absent from SignedAuditRecord (would be dropped at ToSignedShape): {string.Join(", ", missing)}");
    }

    [Fact]
    public void ToSignedShape_ThenToAuditRecord_RoundTripsEveryContentProperty()
    {
        var original = AuditContractSamples.AuditRecord() with { Sandbox = true, DynamicContextEpoch = 3, DynamicContextHash = "h" };

        var signed = AuditRecordHasher.ToSignedShape(original, "prev", "hash", "sig", "key");
        var back = AuditRecordHasher.ToAuditRecord(signed);

        foreach (var property in ContentProperties(typeof(AuditRecord)))
        {
            Assert.True(Equals(property.GetValue(original), property.GetValue(back)) || property.PropertyType.IsGenericType,
                $"{property.Name} did not survive ToSignedShape/ToAuditRecord");
        }
        Assert.Equal(original.Sandbox, back.Sandbox);
        Assert.Equal(original.DynamicContextEpoch, back.DynamicContextEpoch);
        Assert.Equal(original.DynamicContextHash, back.DynamicContextHash);
    }
}
