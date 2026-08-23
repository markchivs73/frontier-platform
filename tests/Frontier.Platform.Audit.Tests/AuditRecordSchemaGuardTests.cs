using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// The guard exists to keep a schema change from masquerading as tampering. Verification
/// recomputes canonical bytes rather than hashing the stored ones, so a record written before
/// the artifact-vocabulary rename would deserialize cleanly, lose the renamed value, and then
/// fail its signature — the signal this system reserves for altered evidence.
/// </summary>
public sealed class AuditRecordSchemaGuardTests
{
    private static SignedAuditRecord RecordWithVersion(string schemaVersion) =>
        AuditContractSamples.SignedAuditRecord() with { SchemaVersion = schemaVersion };

    [Fact]
    public void EnsureReadable_CurrentSchema_ReturnsTheSameRecord()
    {
        var record = RecordWithVersion(AuditRecordSchemaGuard.CurrentSchemaVersion);

        Assert.Same(record, AuditRecordSchemaGuard.EnsureReadable(record));
    }

    [Fact]
    public void EnsureReadable_NewerMinorOfSameMajor_IsReadable()
    {
        // Minor additions stay readable — omit-null defaults cover a field this build has
        // never heard of. Only a major says "the bytes mean something different now".
        var record = RecordWithVersion("2.7");

        Assert.Same(record, AuditRecordSchemaGuard.EnsureReadable(record));
    }

    [Fact]
    public void EnsureReadable_PreRenameRecord_ThrowsRatherThanReturningDegradedEvidence()
    {
        var record = RecordWithVersion("1.0");

        var ex = Assert.Throws<ContractViolationException>(() => AuditRecordSchemaGuard.EnsureReadable(record));

        // The message has to be actionable: an operator seeing this must not conclude tampering.
        Assert.Contains("1.0", ex.Message, StringComparison.Ordinal);
        Assert.Contains("tampering", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureReadable_FutureMajor_AlsoThrows()
    {
        // A record written by a newer build is equally unreadable, and equally must not be
        // mistaken for altered evidence.
        Assert.Throws<ContractViolationException>(() => AuditRecordSchemaGuard.EnsureReadable(RecordWithVersion("3.0")));
    }

    [Fact]
    public void EnsureReadable_NullRecord_Throws() =>
        Assert.Throws<ArgumentNullException>(() => AuditRecordSchemaGuard.EnsureReadable(null!));

    [Theory]
    [InlineData("2.0", "2")]
    [InlineData("10.4", "10")]
    [InlineData("3", "3")]
    public void MajorOf_ParsesTheMajorComponent(string schemaVersion, string expected) =>
        Assert.Equal(expected, AuditRecordSchemaGuard.MajorOf(schemaVersion));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MajorOf_MissingVersion_Throws(string? schemaVersion) =>
        Assert.ThrowsAny<ArgumentException>(() => AuditRecordSchemaGuard.MajorOf(schemaVersion!));
}
