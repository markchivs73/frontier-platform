namespace Frontier.Platform.Abstractions;

/// <summary>
/// The <c>{engagementId}::{workflowId}</c> execution-instance format (invariant 3), stated once.
/// <para>
/// It lives in the kernel because every tier needs it and no lower assembly is visible to them
/// all: the interpreter parses it to consolidate audit, the audit family parses it to derive a
/// partition key, and a workload's composition root mints it. Before this type the format was
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
/// The segments come back as a named tuple rather than a declared type on purpose. Per ADR-PA1 the
/// kernel is inherited by every package and every consumer, so its surface is kept deliberately
/// small; a record carrying two strings would have added fourteen public symbols here to say what
/// a tuple says with none.
/// </para>
/// <para>
/// <b>The engagement id is composite; the workflow id is the last segment.</b> Engagement ids are
/// config-templated (<c>{type}::{client}::{site}</c>, e.g. <c>E2E::Acme::Admin-Website</c>), so an
/// execution id routinely has four or more segments and only the <em>final</em> one is the
/// workflow. Reading from the left returns <c>("E2E", "Acme")</c> — which is what every predecessor
/// of this type did. See ADR-PA12.
/// </para>
/// <para>
/// A dispatcher child appends <c>::{workItemId}</c>, which cannot be told apart from a longer
/// engagement id by inspection. <see cref="Parse"/> is therefore defined for top-level ids only —
/// which is all its callers hold, audit consolidation running against top-level executions — and
/// a child id read here yields the work-item id as its final segment.
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
    /// may not, because it is what makes the id readable back: it is the final segment, and a
    /// workflow id containing the separator would move that boundary.
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

    /// <summary>
    /// Splits an execution id into its leading engagement and workflow segments.
    /// </summary>
    /// <param name="executionId">An execution id in <c>{engagementId}::{workflowId}</c> form.</param>
    /// <returns>The engagement and workflow segments.</returns>
    /// <exception cref="ArgumentException"><paramref name="executionId"/> is not in that form.</exception>
    public static (string EngagementId, string WorkflowId) Parse(string executionId) =>
        ParseOrNull(executionId)
        ?? throw new ArgumentException(
            $"Execution id '{executionId}' is not in '{{engagementId}}{Separator}{{workflowId}}' format.",
            nameof(executionId));

    /// <summary>
    /// Splits an execution id, or returns <see langword="null"/> if it is not in that form — for
    /// callers holding an identifier that may legitimately be something else, so that a malformed
    /// value is a branch rather than an exception.
    /// </summary>
    /// <param name="executionId">A value that may be an execution id.</param>
    /// <returns>The segments, or <see langword="null"/>.</returns>
    public static (string EngagementId, string WorkflowId)? ParseOrNull(string executionId)
    {
        if (string.IsNullOrEmpty(executionId))
        {
            return null;
        }

        // Split at the LAST separator, per doc 16 §3: "the workflow suffix is always the final
        // :: segment of an instance ID, so instance-ID parsing stays unambiguous regardless of
        // how many segments the engagement ID itself has". Reading from the left instead is the
        // defect this type was written to end -- see ADR-PA12.
        var boundary = executionId.LastIndexOf(Separator, StringComparison.Ordinal);
        return boundary <= 0 || boundary + Separator.Length == executionId.Length
            ? null
            : (executionId[..boundary], executionId[(boundary + Separator.Length)..]);
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
