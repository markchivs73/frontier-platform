using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.240 (ADR-PA46): a failed or terminated run's record says why, is covered by its signature, and
/// chains through the guarded append beside completed runs without forking the engagement's chain.
/// </summary>
public sealed class AuditOutcomeTests
{
    private static readonly DevKeyProvider KeyProvider = new();

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("terminated")]
    public void Validate_EachFinalStatusWithItsEvidence_DoesNotThrow(string status)
    {
        Record(status).Validate();
        Signed(status).Validate();
    }

    [Fact]
    public void Validate_FailedWithoutFailure_ThrowsOnBothShapes()
    {
        var record = AuditContractSamples.AuditRecordFailed() with { Failure = null };
        var signed = AuditContractSamples.SignedAuditRecordFailed() with { Failure = null };

        Assert.Contains("failure with a classification is required when final_status is failed.", Assert.Throws<ContractViolationException>(record.Validate).Violations);
        Assert.Contains("failure with a classification is required when final_status is failed.", Assert.Throws<ContractViolationException>(signed.Validate).Violations);
    }

    [Fact]
    public void Validate_FailedWithBlankClassification_Throws()
    {
        var record = AuditContractSamples.AuditRecordFailed() with { Failure = AuditContractSamples.FailureRecord() with { Classification = " " } };

        Assert.Throws<ContractViolationException>(record.Validate);
    }

    [Theory]
    [InlineData(null, "a reason")]
    [InlineData("user:operator-1", " ")]
    [InlineData(" ", "a reason")]
    public void Validate_TerminatedWithoutWhoOrWhy_ThrowsOnBothShapes(string? terminatedBy, string reason)
    {
        TerminationRecord? termination = terminatedBy is null ? null : AuditContractSamples.TerminationRecord() with { TerminatedBy = terminatedBy, Reason = reason };
        var record = AuditContractSamples.AuditRecordTerminated() with { Termination = termination };
        var signed = AuditContractSamples.SignedAuditRecordTerminated() with { Termination = termination };

        Assert.Contains("termination with terminated_by and reason is required when final_status is terminated.", Assert.Throws<ContractViolationException>(record.Validate).Violations);
        Assert.Contains("termination with terminated_by and reason is required when final_status is terminated.", Assert.Throws<ContractViolationException>(signed.Validate).Violations);
    }

    [Fact]
    public void Validate_CompletedRecordCarryingNoOutcomeMembers_IsNotChecked() =>
        AuditContractSamples.AuditRecord().Validate();

    [Fact]
    public void ToSignedShape_CarriesFailureAndTerminationIntoTheHashedBytes()
    {
        var record = AuditRecordHasherTests.Sample() with { FinalStatus = ExecutionStatus.Terminated, Failure = AuditContractSamples.FailureRecord(), Termination = AuditContractSamples.TerminationRecord() };
        var genesis = AuditRecordHasher.ComputeGenesisHash("eng-1");

        var signed = AuditRecordHasher.ToSignedShape(record, genesis, "h", "s", "k");

        Assert.Equal(record.Failure, signed.Failure);
        Assert.Equal(record.Termination, signed.Termination);
        Assert.NotEqual(AuditRecordHasher.ComputeRecordHash(record, genesis), AuditRecordHasher.ComputeRecordHash(record with { Failure = null }, genesis));
        Assert.NotEqual(AuditRecordHasher.ComputeRecordHash(record, genesis), AuditRecordHasher.ComputeRecordHash(record with { Termination = null }, genesis));
    }

    /// <summary>An edit of the failure or the termination after signing is caught by the signature.</summary>
    [Fact]
    public async Task VerifyAsync_FailureOrTerminationEditedAfterSigning_FailsTheSignature()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
        var failed = await signer.SignAsync(Record("failed") with { ExecutionId = "run-failed" }, CancellationToken.None);
        var terminated = await signer.SignAsync(Record("terminated") with { ExecutionId = "run-terminated" }, CancellationToken.None);

        store.Replace(failed with { Failure = failed.Failure! with { Classification = "unclassified" } });
        store.Replace(terminated with { Termination = terminated.Termination! with { TerminatedBy = "user:someone-else" } });

        Assert.False((await signer.VerifyAsync("run-failed", "eng-1", CancellationToken.None)).SignatureValid);
        Assert.False((await signer.VerifyAsync("run-terminated", "eng-1", CancellationToken.None)).SignatureValid);
    }

    /// <summary>
    /// Completed, failed and terminated runs closing on one engagement, through the guarded append the
    /// orchestrator and the consumer's terminate both reach: one linear chain, and every record verifies.
    /// </summary>
    [Fact]
    public async Task SignAsync_MixOfFinalStatusesOnOneEngagement_GivesOneLinearChainThatVerifies()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
        string[] statuses = ["completed", "failed", "terminated", "completed", "terminated", "failed"];

        var signed = new List<SignedAuditRecord>();
        for (var i = 0; i < statuses.Length; i++)
        {
            signed.Add(await signer.SignAsync(Record(statuses[i]) with { ExecutionId = $"run-{i}", ClosedAtUtc = AuditRecordHasherTests.Sample().ClosedAtUtc.AddMinutes(i) }, CancellationToken.None));
        }

        Assert.Equal(AuditRecordHasher.ComputeGenesisHash("eng-1"), signed[0].PreviousRecordHash);
        Assert.All(signed.Skip(1).Zip(signed), pair => Assert.Equal(pair.Second.RecordHash, pair.First.PreviousRecordHash));
        foreach (var record in signed)
        {
            var result = await signer.VerifyAsync(record.ExecutionId, "eng-1", CancellationToken.None);
            Assert.True(result.SignatureValid && result.ChainValid, $"{record.ExecutionId} ({record.FinalStatus.Name}) did not verify: broken at {result.BrokenLinkAt}");
        }
    }

    /// <summary>A record for <paramref name="status"/> carrying the evidence that status requires.</summary>
    private static AuditRecord Record(string status) => status switch
    {
        "failed" => AuditRecordHasherTests.Sample() with { FinalStatus = ExecutionStatus.Failed, Failure = AuditContractSamples.FailureRecord() },
        "terminated" => AuditRecordHasherTests.Sample() with { FinalStatus = ExecutionStatus.Terminated, Termination = AuditContractSamples.TerminationRecord() },
        _ => AuditRecordHasherTests.Sample(),
    };

    /// <summary>The signed shape of <see cref="Record"/>, with placeholder chain fields.</summary>
    private static SignedAuditRecord Signed(string status) =>
        AuditRecordHasher.ToSignedShape(Record(status), "prev", "hash", "sig", "key");

    private static AuditSigner Signer(FakeAuditRecordStore store) =>
        new(store, store, KeyProvider, new HmacAuditSigningService(KeyProvider), Options.Create(new ExecutionAuditOptions
        {
            AppendBaseDelayMs = 0,
            AppendMaxDelayMs = 0,
        }));
}
