using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// S13.22 — <see cref="WorkItem"/> as a validated wire contract (ADR-E2, doc 16 §4).
/// <para>
/// It was a POCO with <c>required object Payload</c> and an
/// <c>[ExcludeFromCodeCoverage(Justification = "… Validate() method not called at runtime")]</c>
/// attribute describing a <c>Validate()</c> that did not exist. Every other contract that crosses
/// this boundary implements <see cref="IVersionedContract"/>; this one carries <b>external</b>
/// input into DTF history, which is the strongest case for validation in the codebase, not the
/// weakest — invariant 7 makes a malformed work item a permanent failure, and something has to
/// classify it as one.
/// </para>
/// <para>
/// <b>The schema-version renumbering is safe, and this is why.</b> Adding <c>schema_version</c> at
/// property order 0 shifts every other property's order by one, which would normally be a
/// compatibility break (canonical-serialization: never renumber a shipped version). No
/// <c>WorkItem</c> has ever been serialized: nothing raises the <c>WorkItem</c> event — that is
/// S13.22 defect 1 — so there are no stored or replayed bytes to break. It is a break only in
/// theory, and only until the first dispatcher runs.
/// </para>
/// </summary>
public sealed class WorkItemTests
{
    [Fact]
    public void Validate_WellFormed_DoesNotThrow() => Sample().Validate();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankWorkItemId_Throws(string workItemId)
    {
        var item = Sample() with { WorkItemId = workItemId };

        var exception = Assert.Throws<ContractViolationException>(item.Validate);

        Assert.Contains("work_item_id must not be blank.", exception.Violations);
    }

    /// <summary>
    /// The envelope's own rules are the work item's rules. A payload claiming both inline content
    /// and a staged reference is malformed wherever it appears, and a work item that accepted it
    /// would hand a child execution a contract the child then has to reject — a permanent failure
    /// discovered one instance too late.
    /// </summary>
    [Fact]
    public void Validate_MalformedPayload_CascadesPrefixedViolations()
    {
        var item = Sample() with { Payload = new TypedPayload { SchemaRef = "" } };

        var exception = Assert.Throws<ContractViolationException>(item.Validate);

        Assert.Contains("payload: schema_ref must not be empty.", exception.Violations);
    }

    /// <summary>
    /// <b>The payload is a typed envelope, not <c>object</c>.</b> Pinned as a type assertion
    /// because a well-meaning revert to <c>object</c> would keep every other test in this file
    /// green — <c>object</c> accepts a <see cref="TypedPayload"/> perfectly well — while silently
    /// restoring the untyped blob and its <c>JsonElement</c> round-trip.
    /// </summary>
    [Fact]
    public void Payload_IsATypedEnvelope() =>
        Assert.Equal(typeof(TypedPayload), typeof(WorkItem).GetProperty(nameof(WorkItem.Payload))!.PropertyType);

    /// <summary>Attribution is optional — a machine-originated ticket has no directing human, and falls back to the dispatcher's initiator at spawn.</summary>
    [Fact]
    public void Validate_NoDirectingHuman_DoesNotThrow() => (Sample() with { DirectedBy = null }).Validate();

    private static WorkItem Sample() => new()
    {
        WorkItemId = "SUB-001",
        Payload = new TypedPayload
        {
            SchemaRef = "schemas/helpdesk-ticket/1.0",
            Payload = System.Text.Json.JsonDocument.Parse("""{"ticket_id":"HDT-1234"}""").RootElement.Clone(),
        },
        DirectedBy = "user:oid-supplier",
    };
}
