using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.103 tests for the governance audit contracts, hashing and small helpers (ADR-PA30).</summary>
public sealed class GovernanceAuditContractTests
{
    [Fact]
    public void Entry_Valid_DoesNotThrow() => Entry().Validate();

    [Fact]
    public void Entry_SystemActorNamingItsOrigin_IsValid() => (Entry() with { Actor = "system:role-sweeper" }).Validate();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData(" UNKNOWN ")]
    [InlineData("system:")]
    public void Entry_UnattributedActor_IsAPermanentViolation(string actor) =>
        AssertViolation(Entry() with { Actor = actor }, "actor");

    [Fact]
    public void Entry_NullActor_IsAViolation() => AssertViolation(Entry() with { Actor = null! }, "actor");

    [Theory]
    [InlineData("")]
    [InlineData("ApproverRoleUpdated")]
    [InlineData("approver-role-updated")]
    [InlineData("1role")]
    [InlineData("role__updated")]
    public void Entry_EventTypeNotSnakeCase_IsAViolation(string eventType) =>
        AssertViolation(Entry() with { EventType = eventType }, "event_type");

    [Fact]
    public void Entry_SubjectTypeNotSnakeCase_IsAViolation() => AssertViolation(Entry() with { SubjectType = "Approver Role" }, "subject_type");

    [Fact]
    public void Entry_ScopeNotSnakeCase_IsAViolation() => AssertViolation(Entry() with { Scope = "Deployment" }, "scope");

    [Fact]
    public void Entry_BlankSubjectId_IsAViolation() => AssertViolation(Entry() with { SubjectId = " " }, "subject_id");

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Entry_BlankReason_IsAViolation(string reason) => AssertViolation(Entry() with { Reason = reason }, "reason");

    [Fact]
    public void Entry_BlankCompensatesRecordId_IsAViolation() => AssertViolation(Entry() with { CompensatesRecordId = " " }, "compensates_record_id");

    [Fact]
    public void Entry_InvalidChangeEnvelope_CascadesPrefixed() =>
        AssertViolation(Entry() with { Change = new TypedPayload { SchemaRef = "schemas/x/1.0" } }, "change: typed_payload");

    [Fact]
    public void Entry_MissingChange_IsAViolation() => AssertViolation(Entry() with { Change = null! }, "change must be present");

    [Fact]
    public void IsSnakeCase_Null_IsFalse() => Assert.False(GovernanceAuditRules.IsSnakeCase(null));

    [Fact]
    public void Record_Valid_DoesNotThrow() => Chain(FakeRotatingKeyProvider.V1)[0].Validate();

    [Fact]
    public void Record_InvalidChainFieldsAndEntryFields_ListsEveryViolation()
    {
        var record = Chain(FakeRotatingKeyProvider.V1)[0] with { Sequence = 0, RecordId = "", Signature = "", Actor = "unknown" };

        var exception = Assert.Throws<ContractViolationException>(record.Validate);

        Assert.Equal(nameof(SignedGovernanceAuditRecord), exception.ContractType);
        Assert.Equal(4, exception.Violations.Count);
    }

    [Fact]
    public void GenesisHash_IsDomainSeparatedFromTheExecutionGenesis()
    {
        var governance = GovernanceAuditHasher.ComputeGenesisHash(GovernanceAuditScopes.Deployment);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("governance:deployment"))), governance);
        Assert.NotEqual(AuditRecordHasher.ComputeGenesisHash(GovernanceAuditScopes.Deployment), governance);
    }

    [Fact]
    public void RecordId_IsDeterministicAndIgnoresPayloadMemberOrder()
    {
        var reordered = Entry() with { Change = Change("""{ "limit": 50000.50, "display_name":"Finance Lead", "responsibilities":["budget-approval","cost-validation"] }""") };

        Assert.Equal(GovernanceAuditHasher.ComputeRecordId(Entry()), GovernanceAuditHasher.ComputeRecordId(Entry()));
        Assert.Equal(GovernanceAuditHasher.ComputeRecordId(Entry()), GovernanceAuditHasher.ComputeRecordId(reordered));
    }

    [Fact]
    public void RecordId_ChangesWithAnyEntryField()
    {
        var id = GovernanceAuditHasher.ComputeRecordId(Entry());

        Assert.NotEqual(id, GovernanceAuditHasher.ComputeRecordId(Entry(reason: "different")));
        Assert.NotEqual(id, GovernanceAuditHasher.ComputeRecordId(Entry() with { OccurredAtUtc = OccurredAt.AddMilliseconds(1) }));
    }

    [Fact]
    public void RecordHash_IgnoresPayloadMemberOrderSoAReserialisedRecordStillVerifies()
    {
        var record = Chain(FakeRotatingKeyProvider.V1)[0];
        var reserialised = record with { Change = Change("""{"limit":50000.5,"responsibilities":["budget-approval","cost-validation"],"display_name":"Finance Lead"}""") };

        Assert.Equal(record.RecordHash, GovernanceAuditHasher.ComputeRecordHash(reserialised));
    }

    [Fact]
    public void RecordHash_CoversTheSigningKeyId()
    {
        var record = Chain(FakeRotatingKeyProvider.V1)[0];

        Assert.NotEqual(record.RecordHash, GovernanceAuditHasher.ComputeRecordHash(record with { SigningKeyId = "dev-key/v2" }));
    }

    [Fact]
    public void ToEntry_RecoversTheEntryTheRecordWasSealedFrom()
    {
        var entry = Entry() with { CompensatesRecordId = "ABC" };

        Assert.Equal(entry, GovernanceAuditHasher.ToEntry(Seal(entry, 4, Genesis, FakeRotatingKeyProvider.V1)));
    }

    [Theory]
    [InlineData(1, 0.0, 50)]
    [InlineData(1, 1.0, 100)]
    [InlineData(3, 0.0, 200)]
    [InlineData(6, 0.0, 500)]
    public void Backoff_DoublesPerAttemptCapsAndJitters(int attempt, double jitter, double expectedMs) =>
        Assert.Equal(expectedMs, GovernanceAuditBackoff.DelayFor(attempt, new GovernanceAuditOptions { AppendBaseDelayMs = 100, AppendMaxDelayMs = 1_000 }, jitter).TotalMilliseconds, 3);

    [Fact]
    public void Backoff_NextJitter_IsInTheUnitInterval()
    {
        var jitter = GovernanceAuditBackoff.NextJitter();

        Assert.InRange(jitter, 0.0, 0.999);
    }

    [Fact]
    public void Options_Defaults_AreSafeAndValid()
    {
        var options = new GovernanceAuditOptions();

        Assert.True(Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true));
        Assert.Equal((8, 25, 1_000), (options.AppendMaxAttempts, options.AppendBaseDelayMs, options.AppendMaxDelayMs));
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void Conflict_OnlyPreconditionAndConflictAreConcurrency(HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, GovernanceAuditConflict.IsConcurrencyConflict(status));

    [Fact]
    public void DocumentIds_AreZeroPaddedSoIdOrderIsSequenceOrder()
    {
        Assert.Equal("deployment:000000000007", GovernanceAuditDocumentId.ForRecord("deployment", 7));
        Assert.True(string.CompareOrdinal(GovernanceAuditDocumentId.ForRecord("deployment", 9), GovernanceAuditDocumentId.ForRecord("deployment", 10)) < 0);
        Assert.Equal("chain-head:deployment", GovernanceAuditDocumentId.ForHead("deployment"));
        Assert.Equal("record-id:ABC", GovernanceAuditDocumentId.ForRecordId("ABC"));
    }

    [Fact]
    public void Documents_FromRecord_CarryTheRecordIdentity()
    {
        var record = Chain(FakeRotatingKeyProvider.V1, FakeRotatingKeyProvider.V1)[1];

        var recordDocument = GovernanceAuditRecordDocument.FromRecord(record);
        var head = GovernanceAuditHeadDocument.FromRecord(record);
        var marker = GovernanceAuditRecordIdDocument.FromRecord(record);

        Assert.Equal(("deployment:000000000002", "deployment", "record"), (recordDocument.Id, recordDocument.Scope, recordDocument.DocType));
        Assert.Same(record, recordDocument.Record);
        Assert.Equal(("chain-head:deployment", "chain_head", 2L, record.RecordHash), (head.Id, head.DocType, head.Sequence, head.RecordHash));
        Assert.Equal(new GovernanceAuditChainHead { Scope = "deployment", Sequence = 2, RecordHash = record.RecordHash }, head.ToHead());
        Assert.Equal(($"record-id:{record.RecordId}", "record_id", record.RecordId, 2L), (marker.Id, marker.DocType, marker.RecordId, marker.Sequence));
    }

    [Fact]
    public void QueryBuilder_NoFilters_SelectsRecordsInSequenceOrder()
    {
        var definition = GovernanceAuditQueryBuilder.Build(new GovernanceAuditQuery());

        Assert.Equal("SELECT * FROM c WHERE c.doc_type = @docType ORDER BY c.record.sequence ASC", definition.QueryText);
        Assert.Single(definition.GetQueryParameters());
    }

    [Fact]
    public void QueryBuilder_EveryFilter_AddsAParameterisedClause()
    {
        var query = new GovernanceAuditQuery
        {
            SubjectType = "approver_role",
            SubjectId = "finance-lead",
            Actor = "user:oid-4f2a",
            EventType = "approver_role_retired",
            FromUtc = OccurredAt,
            ToUtc = OccurredAt.AddDays(1),
        };

        var definition = GovernanceAuditQueryBuilder.Build(query);

        Assert.Equal(
            "SELECT * FROM c WHERE c.doc_type = @docType AND c.record.subject_type = @subjectType AND c.record.subject_id = @subjectId AND c.record.actor = @actor AND c.record.event_type = @eventType AND c.record.occurred_at_utc >= @fromUtc AND c.record.occurred_at_utc <= @toUtc ORDER BY c.record.sequence ASC",
            definition.QueryText);
        Assert.Equal(7, definition.GetQueryParameters().Count);
    }

    [Fact]
    public async Task ArchivalHandler_ExportsSignedRecordsOnly()
    {
        var exporter = new RecordingExporter();
        var handler = new GovernanceAuditArchivalHandler(new ArchivalAuditChangeFeedHandler(exporter, ArchivalGovernanceAuditExportHostedService.BlobContainerName));
        JsonObject[] changes =
        [
            new() { ["id"] = "deployment:000000000001", ["doc_type"] = "record" },
            new() { ["id"] = "chain-head:deployment", ["doc_type"] = "chain_head" },
            new() { ["id"] = "record-id:ABC", ["doc_type"] = "record_id" },
            new() { ["id"] = "untyped" },
        ];

        await handler.HandleChangesAsync(changes, CancellationToken.None);

        var (container, blob) = Assert.Single(exporter.Exported);
        Assert.Equal(("governance-audit-records-archive", "deployment:000000000001"), (container, blob));
    }

    [Fact]
    public void SharedKeyRing_GivesEveryPurposeTheOneProvider()
    {
        var provider = new FakeRotatingKeyProvider();
        var ring = new SharedSigningKeyRing(provider);

        Assert.All(SigningKeyPurpose.List, purpose => Assert.Same(provider, ring.GetProvider(purpose)));
        Assert.Throws<ArgumentNullException>(() => ring.GetProvider(null!));
        Assert.Equal(["execution_audit", "governance_audit"], SigningKeyPurpose.List.Select(purpose => purpose.Name));
    }

    [Fact]
    public void BreakKinds_HaveStableWireNames() =>
        Assert.Equal(
            ["signature_mismatch", "sequence_gap", "out_of_order", "hash_link_break", "unresolved_key", "scope_mismatch", "head_mismatch"],
            GovernanceAuditBreakKind.List.Select(kind => kind.Name));

    [Fact]
    public void AppendException_Constructors_CarryTheirMessage()
    {
        var inner = new InvalidOperationException("inner");

        Assert.NotNull(new GovernanceAuditAppendException().Message);
        Assert.Equal("m", new GovernanceAuditAppendException("m").Message);
        Assert.Same(inner, new GovernanceAuditAppendException("m", inner).InnerException);
    }

    private static void AssertViolation(GovernanceAuditEntry entry, string expectedFragment)
    {
        var exception = Assert.Throws<ContractViolationException>(entry.Validate);
        Assert.Contains(exception.Violations, violation => violation.Contains(expectedFragment, StringComparison.Ordinal));
    }

    private sealed class RecordingExporter : IAuditRecordExporter
    {
        internal List<(string Container, string Blob)> Exported { get; } = [];

        public Task ExportAsync(string containerName, string blobName, ReadOnlyMemory<byte> recordBytes, CancellationToken cancellationToken)
        {
            Exported.Add((containerName, blobName));
            return Task.CompletedTask;
        }
    }
}
