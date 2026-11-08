namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// An approval was refused because the proposed revision no longer passes the rule catalogue against
/// the live registries (doc 13 §3: "a stale-but-passing report from last week cannot publish against
/// this week's registries"). For example, a tool it calls was retired or its server drifted after the
/// proposal was submitted. Nothing was written: the proposal stays in review and no version exists.
/// Derives from <see cref="InvalidOperationException"/>, so a caller that already maps a lifecycle
/// refusal keeps doing so, and can read <see cref="Findings"/> to say why.
/// </summary>
public sealed class PublishRevalidationException : InvalidOperationException
{
    /// <summary>Creates an empty exception (CA1032); prefer the (proposalId, findings) constructor.</summary>
    public PublishRevalidationException()
    {
        Findings = [];
    }

    /// <summary>Creates the exception with a free-text message (CA1032); prefer the (proposalId, findings) constructor.</summary>
    public PublishRevalidationException(string message)
        : base(message)
    {
        Findings = [];
    }

    /// <summary>Creates the exception with a free-text message and inner exception (CA1032); prefer the (proposalId, findings) constructor.</summary>
    public PublishRevalidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Findings = [];
    }

    /// <summary>Creates the exception for <paramref name="proposalId"/>, naming each blocking finding in the message.</summary>
    public PublishRevalidationException(string proposalId, IReadOnlyList<ValidationFinding> findings)
        : base(PublishRevalidationMessage.For(proposalId, findings))
    {
        Findings = findings;
    }

    /// <summary>The error findings the approve-time validation returned; empty only for the CA1032 constructors.</summary>
    public IReadOnlyList<ValidationFinding> Findings { get; }
}

/// <summary>The message a <see cref="PublishRevalidationException"/> carries: the proposal, then each finding's rule, node and text.</summary>
internal static class PublishRevalidationMessage
{
    /// <summary>Formats the refusal for <paramref name="proposalId"/>.</summary>
    internal static string For(string proposalId, IReadOnlyList<ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var reasons = string.Join("; ", findings.Select(f => f.NodeId is null ? $"[{f.RuleId}] {f.Message}" : $"[{f.RuleId}] {f.NodeId}: {f.Message}"));
        return $"Proposal {proposalId} no longer passes validation against the live registries, so it cannot be approved. Re-validate and re-propose. {reasons}";
    }
}
