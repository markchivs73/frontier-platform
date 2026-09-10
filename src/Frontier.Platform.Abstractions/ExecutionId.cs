namespace Frontier.Platform.Abstractions;

/// <summary>
/// The two keys an execution has, stated once: the <b>affinity key</b>
/// <c>{engagementId}::{workflowId}</c> (invariant 3's engagement-workflow identity, what one live
/// run per engagement-workflow is enforced on) and the <b>instance id</b>, which since ADR-PA20 is
/// the run token alone.
/// <para>
/// It lives in the kernel because every tier needs to <em>mint</em> these and no lower assembly is
/// visible to them all: a workload's composition root mints an id for scheduling, and the
/// interpreter and audit family address durable state by it. Before this type the format was
/// written out in six places across two repositories — twice inside this one — because each
/// assembly boundary the code crossed made the previous <c>internal</c> helper unreachable and a
/// fresh copy the path of least resistance.
/// </para>
/// <para>
/// <b>An execution id is written, never read.</b> It is an addressing key for durable state, and
/// nothing may split, slice or pattern-match it to recover anything. Identity travels as typed
/// fields on the contracts instead — <c>ExecutionSnapshot</c>, <c>ConsolidateAuditInput</c> and
/// <c>AuditRecord</c> each carry <c>EngagementId</c>, <c>WorkflowId</c> and <c>RunId</c> explicitly —
/// because a composite string is a contract nobody validates. The parsing this type used to
/// publish is gone (ADR-PA15), and with ADR-PA20 there is nothing left in the id to parse: the
/// engagement and workflow it used to spell out are the fields.
/// </para>
/// <para>
/// <b>Why the instance id stopped carrying the affinity key (ADR-PA20).</b> Durable Task Scheduler
/// caps an instance id at <see cref="MaxInstanceIdLength"/> characters. Prefixing a 32-character run
/// token with <c>{engagementId}::{workflowId}</c> left 65 characters for two ids that are both
/// caller-shaped — a GUID workflow id alone is 36, and engagement ids are composite and
/// config-templated — so a sandbox run of any UI-authored workflow, and a real run on any templated
/// engagement id past 29 characters, failed at scheduling with "Instance IDs must be between 1 and
/// 100 characters". The composite was metadata riding in the address; ADR-PA15 had already said
/// the address must not be read, which is what makes carrying nothing in it free.
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
    /// <summary>Separates the segments of an affinity key.</summary>
    public const string Separator = "::";

    /// <summary>
    /// The longest instance id Durable Task Scheduler schedules: orchestration instance ids must be
    /// between 1 and 100 printable-ASCII characters, and must not contain <c>/ \ # ?</c>
    /// (Microsoft Learn, "Durable orchestrations overview — instance ids"; enforced client-side by
    /// <c>Microsoft.DurableTask.Client</c> 1.24). Stated here so the limit is a named rule rather
    /// than an exception message discovered at the first long id.
    /// </summary>
    public const int MaxInstanceIdLength = 100;

    /// <summary>
    /// Builds the <b>affinity key</b> for an engagement's workflow: <c>{engagementId}::{workflowId}</c>.
    /// <para>
    /// This is the derivable value a claim is taken on to enforce one <em>live</em> run per
    /// engagement-workflow (ADR-PA16, K9). It is a Cosmos document id, never a DTS instance id, so
    /// its length is bounded by Cosmos (255), not by <see cref="MaxInstanceIdLength"/>.
    /// </para>
    /// <para>
    /// The engagement id may itself contain <see cref="Separator"/> and usually does — engagement
    /// ids are composite and config-templated (<c>{type}::{client}::{site}</c>). The workflow id
    /// may not: a single segment keeps the minted key predictable for the claim.
    /// </para>
    /// </summary>
    /// <param name="engagementId">The engagement the execution belongs to; may be composite.</param>
    /// <param name="workflowId">The workflow being executed; a single segment.</param>
    /// <returns>The <c>{engagementId}::{workflowId}</c> affinity key.</returns>
    public static string Mint(string engagementId, string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ThrowIfNotASingleSegment(workflowId, nameof(workflowId));

        return string.Concat(engagementId, Separator, workflowId);
    }

    /// <summary>
    /// The <b>instance id</b> for one run of an engagement's workflow: the run token itself
    /// (ADR-PA20). The token is what DTF schedules against, what every snapshot, gate event and
    /// audit record is addressed by, and what <c>RunId</c> carries as a field — one value, not two
    /// spellings of it.
    /// <para>
    /// The token is supplied rather than generated here: <see cref="Frontier.Platform.Abstractions"/>
    /// is the zero-dependency kernel (ADR-PA1), and generation is the composition root's job — it
    /// must happen before scheduling and never inside an orchestrator body, where non-deterministic
    /// values are banned outright. What the kernel does is refuse a token DTS would refuse, or one
    /// that would read as an affinity key.
    /// </para>
    /// </summary>
    /// <param name="runToken">Discriminates this run from every other run, of any engagement-workflow. A single segment of at most <see cref="MaxInstanceIdLength"/> characters.</param>
    /// <returns>The instance id — <paramref name="runToken"/>, validated.</returns>
    public static string ForRun(string runToken)
    {
        ThrowIfNotASingleSegment(runToken, nameof(runToken));

        if (runToken.Length > MaxInstanceIdLength)
        {
            throw new ArgumentException(
                $"A run token cannot be an instance id: it is {runToken.Length} characters and Durable Task Scheduler allows at most {MaxInstanceIdLength}.",
                nameof(runToken));
        }

        return runToken;
    }

    internal static void ThrowIfNotASingleSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{value}' cannot be a single segment of an execution key: it contains the '{Separator}' separator.",
                parameterName);
        }
    }
}
