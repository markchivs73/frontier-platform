using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.6 tests for <see cref="ConsolidateAuditActivity"/>'s delegation to <see cref="IAuditConsolidator"/> and <see cref="IAuditSigner"/>.</summary>
public sealed class ConsolidateAuditActivityTests
{
    private readonly FakeAuditConsolidator consolidator = new();
    private readonly FakeAuditSigner signer = new();

    [Fact]
    public async Task RunAsync_DelegatesToConsolidatorThenSigner_ReturnsSignedRecord()
    {
        var activity = new ConsolidateAuditActivity(consolidator, signer);
        var input = new ConsolidateAuditInput
        {
            ExecutionId = "eng-1::wf-chain",
            DefinitionHash = "definition-hash",
            StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await activity.RunAsync(new FakeTaskActivityContext(), input);

        Assert.Same(input, Assert.Single(consolidator.Inputs));
        var consolidated = Assert.Single(signer.SignedRecords);
        Assert.Equal(input.ExecutionId, consolidated.ExecutionId);
        Assert.Equal(input.DefinitionHash, consolidated.DefinitionHash);
        Assert.Equal(input.StartedAtUtc, consolidated.StartedAtUtc);
        Assert.Equal(consolidated.ExecutionId, result.ExecutionId);
        Assert.Equal(consolidated.DefinitionHash, result.DefinitionHash);
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var activity = new ConsolidateAuditActivity(consolidator, signer);

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(new FakeTaskActivityContext(), null!));
    }
}
