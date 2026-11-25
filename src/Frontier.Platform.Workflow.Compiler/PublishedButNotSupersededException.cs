namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// An approval landed and its version is published and current, but marking the versions it replaces
/// <c>superseded</c> did not finish (consumer S13.350a, ADR-PA50). The caller must not treat this as
/// "the approval did not happen": a governance trail that compensates the approval would contradict the
/// published version. Deliberately not an <see cref="InvalidOperationException"/>, which this library throws
/// only when nothing was written. The versions left are retried by the workflow's next publish; until then a
/// retired one among them that is un-retired becomes published beside the new version (GOV-14).
/// <see cref="Exception.InnerException"/> is what stopped the supersede: a store fault, or the caller's
/// cancellation.
/// </summary>
public sealed class PublishedButNotSupersededException : Exception
{
    /// <summary>Creates an empty exception (CA1032); prefer the (published, unsuperseded, inner) constructor.</summary>
    public PublishedButNotSupersededException()
    {
        UnsupersededVersions = [];
    }

    /// <summary>Creates the exception with a free-text message (CA1032); prefer the (published, unsuperseded, inner) constructor.</summary>
    public PublishedButNotSupersededException(string message)
        : base(message)
    {
        UnsupersededVersions = [];
    }

    /// <summary>Creates the exception with a free-text message and inner exception (CA1032); prefer the (published, unsuperseded, inner) constructor.</summary>
    public PublishedButNotSupersededException(string message, Exception innerException)
        : base(message, innerException)
    {
        UnsupersededVersions = [];
    }

    /// <summary>Creates the exception for <paramref name="published"/>, naming the versions <paramref name="unsupersededVersions"/> still to supersede.</summary>
    public PublishedButNotSupersededException(PublishedVersion published, IReadOnlyList<int> unsupersededVersions, Exception innerException)
        : base(PublishedButNotSupersededMessage.For(published, unsupersededVersions), innerException)
    {
        Published = published;
        UnsupersededVersions = unsupersededVersions;
    }

    /// <summary>The version that was published and is current, as <c>ApproveAsync</c> would have returned it; null only for the CA1032 constructors.</summary>
    public PublishedVersion? Published { get; }

    /// <summary>The earlier versions not confirmed superseded, ascending; empty only for the CA1032 constructors.</summary>
    public IReadOnlyList<int> UnsupersededVersions { get; }
}

/// <summary>The message a <see cref="PublishedButNotSupersededException"/> carries.</summary>
internal static class PublishedButNotSupersededMessage
{
    /// <summary>Says what was published, which versions are left, and that the next publish retries them.</summary>
    internal static string For(PublishedVersion published, IReadOnlyList<int> unsupersededVersions)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(unsupersededVersions);
        return $"Workflow {published.WorkflowId} version {published.DefinitionVersion} is published and current, but versions "
            + $"{string.Join(", ", unsupersededVersions)} were not marked superseded. The next publish retries them.";
    }
}
