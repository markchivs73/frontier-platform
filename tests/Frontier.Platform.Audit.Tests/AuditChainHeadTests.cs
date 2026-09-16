using System.Net;
using System.Text;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.106 tests for the chain-head document, its id, and the conflict classification (ADR-PA31).
/// These types are driven in production by <see cref="CosmosAuditRecordStore"/>, which is excluded
/// from the coverage gate as an SDK adapter — so they are pinned directly here rather than left to
/// the emulator suite.
/// </summary>
public sealed class AuditChainHeadTests
{
    private static AuditChainHead Head() => new("eng-1", 4, "RECORD-HASH", "eng-1::wf-4", UnguardedRecordCount: 2);

    [Fact]
    public void ForHead_BuildsThePrefixedId() =>
        Assert.Equal("chain-head:eng-1", AuditRecordDocumentId.ForHead("eng-1"));

    [Fact]
    public void ForHead_CannotCollideWithARecordId()
    {
        // Every record id ends in ":audit"; the head's prefix keeps the two apart in one partition.
        Assert.NotEqual(AuditRecordDocumentId.ForExecution("eng-1"), AuditRecordDocumentId.ForHead("eng-1"));
        Assert.StartsWith("chain-head:", AuditRecordDocumentId.ForHead("eng-1"), StringComparison.Ordinal);
    }

    [Fact]
    public void FromHead_CarriesEveryFieldAndNeverExpires()
    {
        var document = AuditChainHeadDocument.FromHead(Head());

        Assert.Equal("chain-head:eng-1", document.Id);
        Assert.Equal("eng-1", document.EngagementId);
        Assert.Equal(AuditRecordDocumentId.HeadDocType, document.DocType);
        Assert.Equal(4, document.Sequence);
        Assert.Equal("RECORD-HASH", document.RecordHash);
        Assert.Equal("eng-1::wf-4", document.LastExecutionId);
        Assert.Equal(2, document.UnguardedRecordCount);
        Assert.Equal(SignedAuditRecordDocument.NeverExpires, document.Ttl);
    }

    [Fact]
    public void FromHead_ThenToHead_RoundTrips() =>
        Assert.Equal(Head(), AuditChainHeadDocument.FromHead(Head()).ToHead());

    [Fact]
    public void HeadDocument_SerializesWithCanonicalSnakeCaseNames()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(AuditChainHeadDocument.FromHead(Head())));

        Assert.Equal(
            """{"id":"chain-head:eng-1","engagement_id":"eng-1","doc_type":"chain_head","sequence":4,"record_hash":"RECORD-HASH","last_execution_id":"eng-1::wf-4","unguarded_record_count":2,"ttl":-1}""",
            json);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.FailedDependency, false)]
    public void IsConcurrencyConflict_ClassifiesOnlyTheLostRaceStatuses(HttpStatusCode statusCode, bool expected) =>
        Assert.Equal(expected, AuditChainConflict.IsConcurrencyConflict(statusCode));

    [Fact]
    public void AppendException_ExposesTheStandardConstructors()
    {
        var inner = new InvalidOperationException("inner");

        Assert.NotNull(new AuditChainAppendException().Message);
        Assert.Equal("boom", new AuditChainAppendException("boom").Message);
        Assert.Same(inner, new AuditChainAppendException("boom", inner).InnerException);
    }
}
