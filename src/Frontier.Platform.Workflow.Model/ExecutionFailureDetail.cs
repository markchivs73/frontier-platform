using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Why an execution paused on a failure, as the orchestrator observed it (S13.120): the node
/// whose task faulted first, the exception's type, and its message — bounded, and never a
/// stack trace.
/// <para>
/// <see cref="ExecutionSnapshot.FailureClassification"/> says <em>what kind</em> of failure
/// paused the run (doc 10 §3's taxonomy); this says <em>which</em> one. Without it the reason
/// lived only in durable history and the worker's console, so diagnosing a paused run meant
/// reading its artifacts by hand or reproducing the call.
/// </para>
/// <para>
/// <b>Secret-free by construction.</b> Only the exception's <c>Message</c> is taken — never its
/// <c>ToString()</c>, its stack trace or its data — the result is bounded to
/// <see cref="MaxMessageLength"/> characters, and credential-shaped fragments (<c>Bearer …</c>,
/// <c>sig=…</c>, <c>key=…</c> and kin) are redacted before the detail is written, because a
/// projection is read by every visitor to the execution screen and must never carry a bearer
/// credential (ADR-SEC5).
/// </para>
/// </summary>
public sealed record ExecutionFailureDetail
{
    /// <summary>The longest <see cref="Message"/> a detail may carry; a longer message is cut and ends in an ellipsis.</summary>
    public const int MaxMessageLength = 512;

    /// <summary>The node whose task faulted first — the node <c>paused_on_failure</c> is attributed to.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The exception's full type name as the activity threw it — the type doc 10 §3's classification was read from.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("exception_type")]
    public required string ExceptionType { get; init; }

    /// <summary>The exception's message, bounded and redacted; an inner failure's message follows an arrow.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>The violations this detail carries, named by wire field — folded into the owning snapshot's <see cref="ExecutionSnapshot.Validate"/>.</summary>
    internal IEnumerable<string> Violations()
    {
        if (string.IsNullOrWhiteSpace(NodeId))
        {
            yield return "failure_detail.node_id must not be blank.";
        }

        if (string.IsNullOrWhiteSpace(ExceptionType))
        {
            yield return "failure_detail.exception_type must not be blank.";
        }

        if (Message is { Length: > MaxMessageLength })
        {
            yield return $"failure_detail.message must be at most {MaxMessageLength} characters.";
        }
    }
}
