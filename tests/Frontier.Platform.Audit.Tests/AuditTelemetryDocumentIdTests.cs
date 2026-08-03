namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.2 tests for <see cref="AuditTelemetryDocumentId"/>'s deterministic id formatting (doc 05 §9).</summary>
public sealed class AuditTelemetryDocumentIdTests
{
    [Fact]
    public void ForInvocation_FormatsExecutionIdAndCorrelationId()
    {
        var id = AuditTelemetryDocumentId.ForInvocation("eng-1::wf-chain", "corr-3");

        Assert.Equal("eng-1::wf-chain:corr-3", id);
    }
}
