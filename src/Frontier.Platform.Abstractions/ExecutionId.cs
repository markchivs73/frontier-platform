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
/// </summary>
public static class ExecutionId
{
    /// <summary>Separates the segments of an execution id.</summary>
    public const string Separator = "::";

    /// <summary>
    /// Builds the execution id for an engagement's workflow. Neither segment may itself contain
    /// <see cref="Separator"/>: the id would still be well-formed to look at, but would parse
    /// back to different values than were minted.
    /// </summary>
    /// <param name="engagementId">The engagement the execution belongs to.</param>
    /// <param name="workflowId">The workflow being executed.</param>
    /// <returns>The <c>{engagementId}::{workflowId}</c> instance id.</returns>
    public static string Mint(string engagementId, string workflowId)
    {
        ThrowIfNotASegment(engagementId, nameof(engagementId));
        ThrowIfNotASegment(workflowId, nameof(workflowId));

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

        // A dispatcher child appends ::{workItemId}; the extra segment is deliberately ignored,
        // so a child id resolves to the same engagement and workflow as its parent.
        var parts = executionId.Split(Separator);
        return parts.Length < 2 ? null : (parts[0], parts[1]);
    }

    internal static void ThrowIfNotASegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{value}' cannot be part of an execution id: it contains the '{Separator}' separator.",
                parameterName);
        }
    }
}
