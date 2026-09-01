namespace Frontier.Platform.Abstractions;

/// <summary>
/// The <c>{engagementId}::{workflowId}</c> execution-instance format (invariant 3), stated once.
/// <para>
/// It lives in the kernel because every tier needs to <em>mint</em> it and no lower assembly is
/// visible to them all: a workload's composition root mints an id for scheduling, and the
/// interpreter and audit family address durable state by it. Before this type the format was
/// written out in six places across two repositories — twice inside this one — because each
/// assembly boundary the code crossed made the previous <c>internal</c> helper unreachable and a
/// fresh copy the path of least resistance.
/// </para>
/// <para>
/// Publishing <see cref="Mint"/> does not loosen invariant 3, which governs who may mint an id
/// *for scheduling*, not who may know the format. A named call is materially easier to police
/// than the string shapes that preceded it — <c>{engagementId}::{workflowId}</c> is
/// indistinguishable from any other two-part composite key by inspection, and a call to
/// <see cref="Mint"/> is not.
/// </para>
/// <para>
/// <b>An execution id is written, never read.</b> It is an addressing key for durable state, and
/// nothing may split, slice or pattern-match it to recover the parts that went into it. Identity
/// travels as typed fields on the contracts instead — <c>ExecutionSnapshot</c>,
/// <c>ConsolidateAuditInput</c> and <c>AuditRecord</c> each carry <c>EngagementId</c> and
/// <c>WorkflowId</c> explicitly — because a composite string is a contract nobody validates. The
/// parsing this type used to publish is gone (ADR-PA15).
/// </para>
/// <para>
/// The removed readers are worth remembering rather than rediscovering. Every one of them wanted
/// a single value, the engagement id, to derive a Cosmos partition key — and every caller already
/// held it typed. Reading it back out instead produced ADR-PA12 (splitting from the left returned
/// the engagement's *type* as the engagement and the client as the workflow, mis-partitioning every
/// audit record for a composite engagement id) and left dispatcher child ids
/// (<c>::{workItemId}</c>) permanently ambiguous against longer engagement ids. Both defects were
/// in the reading, never in the format.
/// </para>
/// </summary>
public static class ExecutionId
{
    /// <summary>Separates the segments of an execution id.</summary>
    public const string Separator = "::";

    /// <summary>
    /// Builds the execution id for an engagement's workflow.
    /// <para>
    /// The engagement id may itself contain <see cref="Separator"/> and usually does — engagement
    /// ids are composite and config-templated (<c>{type}::{client}::{site}</c>). The workflow id
    /// may not: a single segment keeps the minted id predictable for the affinity claim that
    /// enforces one live instance per engagement-workflow.
    /// </para>
    /// </summary>
    /// <param name="engagementId">The engagement the execution belongs to; may be composite.</param>
    /// <param name="workflowId">The workflow being executed; a single segment.</param>
    /// <returns>The <c>{engagementId}::{workflowId}</c> instance id.</returns>
    public static string Mint(string engagementId, string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ThrowIfNotASingleSegment(workflowId, nameof(workflowId));

        return string.Concat(engagementId, Separator, workflowId);
    }

    internal static void ThrowIfNotASingleSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{value}' cannot be the final segment of an execution id: it contains the '{Separator}' separator.",
                parameterName);
        }
    }
}
