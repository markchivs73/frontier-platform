using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S13.350a: the exception an approval raises when it published but could not supersede the versions it replaces.</summary>
public sealed class PublishedButNotSupersededExceptionTests
{
    private static readonly PublishedVersion Published = new("wf-1", 4, "sha256:x", "user:proposer", "user:approver", new DateTime(2026, 11, 25, 1, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ForAPublish_SaysItIsPublished_NamesTheVersionsLeft_AndThatTheNextPublishRetriesThem()
    {
        var inner = new TimeoutException("slow");

        var ex = new PublishedButNotSupersededException(Published, [2, 3], inner);

        Assert.Same(Published, ex.Published);
        Assert.Equal([2, 3], ex.UnsupersededVersions);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal(
            "Workflow wf-1 version 4 is published and current, but versions 2, 3 were not marked superseded. The next publish retries them.",
            ex.Message);
    }

    [Fact]
    public void ForAPublish_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PublishedButNotSupersededException(null!, [1], new TimeoutException()));
        Assert.Throws<ArgumentNullException>(() => new PublishedButNotSupersededException(Published, null!, new TimeoutException()));
    }

    [Fact]
    public void StandardConstructors_CarryNoPublishAndNoVersions()
    {
        var inner = new InvalidOperationException("inner");
        var wrapped = new PublishedButNotSupersededException("m", inner);

        Assert.Null(new PublishedButNotSupersededException().Published);
        Assert.Empty(new PublishedButNotSupersededException().UnsupersededVersions);
        Assert.Equal("m", new PublishedButNotSupersededException("m").Message);
        Assert.Empty(new PublishedButNotSupersededException("m").UnsupersededVersions);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Null(wrapped.Published);
        Assert.Empty(wrapped.UnsupersededVersions);
    }
}
