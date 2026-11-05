using System.Net;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// <see cref="ModelRoleConfigWrites.AllocationOutcome"/> — what a mapping- or definition-version allocation batch's
/// status means (ADR-PA32, ADR-PA40). It sat inside the coverage-excluded stores until S13.193.
/// </summary>
public sealed class ModelRoleConfigWritesTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public void AllocationOutcome_BatchLanded_ReturnsTheProposalETag(HttpStatusCode status) =>
        Assert.Equal("etag-2", ModelRoleConfigWrites.AllocationOutcome(status, () => "etag-2", Unreachable));

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public void AllocationOutcome_AnotherWriterWon_IsNullWithoutReadingTheETag(HttpStatusCode status) =>
        Assert.Null(ModelRoleConfigWrites.AllocationOutcome(status, () => throw new InvalidOperationException("read on a lost race"), Unreachable));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)199)]
    public void AllocationOutcome_AnyOtherFailure_ThrowsTheDescribedGovernanceError(HttpStatusCode status)
    {
        var ex = Assert.Throws<MappingGovernanceException>(() => ModelRoleConfigWrites.AllocationOutcome(status, () => "etag", () => "batch failed"));

        Assert.Equal("batch failed", ex.Message);
    }

    private static string Unreachable() => throw new InvalidOperationException("described a failure that did not happen");
}
