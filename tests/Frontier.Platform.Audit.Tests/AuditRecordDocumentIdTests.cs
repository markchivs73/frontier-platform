namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditRecordDocumentId"/>'s deterministic id formatting (doc 05 §6).</summary>
public sealed class AuditRecordDocumentIdTests
{
    [Fact]
    public void ForExecution_FormatsExecutionIdWithAuditSuffix()
    {
        var id = AuditRecordDocumentId.ForExecution("eng-1::wf-chain");

        Assert.Equal("eng-1::wf-chain:audit", id);
    }
}
