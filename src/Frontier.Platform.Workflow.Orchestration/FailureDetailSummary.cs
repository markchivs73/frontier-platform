using System.Text.RegularExpressions;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Builds the <see cref="ExecutionFailureDetail"/> a <c>paused_on_failure</c> checkpoint carries
/// (S13.120) from the exception <see cref="GraphWalk.FirstFailure"/> recorded — a pure function
/// of that exception, so it is replay-safe inside the orchestrator body (hard invariant 2).
/// <para>
/// An activity failure crosses the DTF boundary as a <see cref="TaskFailedException"/> whose
/// <see cref="TaskFailureDetails"/> chain describes the activity's own exception, so the type and
/// message are read from the chain's head — the level <see cref="GraphOrchestratorSteps.ClassifyFailure"/>
/// reads first — and the inner failures' messages follow, joined by <see cref="InnerSeparator"/>.
/// An exception thrown in the body itself is read directly. Only messages are taken — never
/// <see cref="Exception.ToString"/>, a stack trace or exception data — credential-shaped fragments
/// are redacted (ADR-SEC5), and the result is bounded to
/// <see cref="ExecutionFailureDetail.MaxMessageLength"/>.
/// </para>
/// </summary>
internal static partial class FailureDetailSummary
{
    /// <summary>Joins an outer failure's message to its inner failure's.</summary>
    internal const string InnerSeparator = " → ";

    /// <summary>What replaces the value of a credential-shaped fragment.</summary>
    internal const string Redacted = "[redacted]";

    /// <summary>Describes <paramref name="exception"/>, the first failure observed at <paramref name="nodeId"/>.</summary>
    internal static ExecutionFailureDetail Describe(string nodeId, Exception exception) => exception switch
    {
        TaskFailedException failed => Describe(nodeId, failed.FailureDetails.ErrorType, Messages(failed.FailureDetails)),
        _ => Describe(nodeId, exception.GetType().ToString(), exception.Message),
    };

    /// <summary>Builds the detail from its parts, redacting then bounding <paramref name="message"/>.</summary>
    internal static ExecutionFailureDetail Describe(string nodeId, string exceptionType, string message) => new()
    {
        NodeId = nodeId,
        ExceptionType = exceptionType,
        Message = Bound(Redact(message)),
    };

    /// <summary>The chain's messages, outermost first — each level names only itself, so the chain is walked.</summary>
    internal static string Messages(TaskFailureDetails details) =>
        details.InnerFailure is { } inner
            ? details.ErrorMessage + InnerSeparator + Messages(inner)
            : details.ErrorMessage;

    /// <summary>Cuts <paramref name="message"/> to <see cref="ExecutionFailureDetail.MaxMessageLength"/>, ending in an ellipsis when it was longer.</summary>
    internal static string Bound(string message) =>
        message.Length <= ExecutionFailureDetail.MaxMessageLength
            ? message
            : message[..(ExecutionFailureDetail.MaxMessageLength - 1)] + "…";

    /// <summary>
    /// Replaces the value of every credential-shaped fragment in <paramref name="message"/> with
    /// <see cref="Redacted"/>: an <c>Authorization</c> scheme's token (<c>Bearer …</c>, <c>Basic …</c>
    /// followed by something token-shaped — eight or more token characters that are not a plain
    /// word, so "the bearer of this message" is left alone) and a query or key-value credential
    /// (<c>sig=</c>, <c>key=</c>, <c>api_key=</c>, <c>token=</c>, <c>secret=</c>, <c>password=</c>
    /// and their spellings). The name is kept so the message still says what kind of thing was there.
    /// </summary>
    internal static string Redact(string message) => CredentialFragment().Replace(message, "${name}" + Redacted);

    // A token: 8+ of the characters bearer/base64 tokens are made of, containing a digit, a
    // symbol, or an upper-case letter past its first character — which no English word does.
    [GeneratedRegex(
        @"(?<name>\b(?:Bearer|Basic)\s+)(?=[A-Za-z0-9\-._~+/=]*[0-9\-._~+/=]|[A-Za-z0-9\-._~+/=]+(?-i:[A-Z]))[A-Za-z0-9\-._~+/=]{8,}"
        + @"|(?<name>\b(?:sig|key|api[-_]?key|access[-_]?token|token|secret|client[-_]?secret|password|pwd)=)[^\s&;""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialFragment();
}
