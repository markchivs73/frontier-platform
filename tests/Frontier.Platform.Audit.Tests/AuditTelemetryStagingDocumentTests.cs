using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.2 tests for <see cref="AuditTelemetryStagingDocument"/> (doc 05 §9, C-14).</summary>
public sealed class AuditTelemetryStagingDocumentTests
{
    [Fact]
    public void FromRecord_SetsDeterministicIdAndPartitionKey()
    {
        var record = TelemetrySamples.Record();

        var document = AuditTelemetryStagingDocument.FromRecord(record);

        Assert.Equal("eng-1::wf-chain:corr-3", document.Id);
        Assert.Equal(record.ExecutionId, document.ExecutionId);
        Assert.Same(record, document.Record);
    }

}
