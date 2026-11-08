using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S13.248: the refusal an approval raises when the revision no longer passes validation.</summary>
public sealed class PublishRevalidationExceptionTests
{
    [Fact]
    public void ForFindings_NamesTheProposalAndEachFinding_WithItsNodeWhenItHasOne()
    {
        IReadOnlyList<ValidationFinding> findings =
        [
            new("mcp.server-not-drifted", ValidationSeverity.Error, "server drifted", NodeId: "m1"),
            new("schema.version-supported", ValidationSeverity.Error, "too new"),
        ];

        var ex = new PublishRevalidationException("prop-7", findings);

        Assert.Same(findings, ex.Findings);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.StartsWith("Proposal prop-7 no longer passes validation against the live registries", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith("[mcp.server-not-drifted] m1: server drifted; [schema.version-supported] too new", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForFindings_NullFindings_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new PublishRevalidationException("prop-7", (IReadOnlyList<ValidationFinding>)null!));

    [Fact]
    public void StandardConstructors_CarryNoFindings()
    {
        var inner = new InvalidOperationException("inner");

        Assert.Empty(new PublishRevalidationException().Findings);
        Assert.Equal("m", new PublishRevalidationException("m").Message);
        var wrapped = new PublishRevalidationException("m", inner);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Empty(wrapped.Findings);
        Assert.Empty(new PublishRevalidationException("m").Findings);
    }
}
