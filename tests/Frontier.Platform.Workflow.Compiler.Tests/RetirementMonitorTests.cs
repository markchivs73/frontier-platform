using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.223 (GOV-15): ADR-DC4's retirement candidate query is deferred. The shipped types stay until
/// the next phase boundary, obsolete, and the monitor refuses to answer rather than report that no
/// version is a candidate. The class is obsolete because every member names the obsolete types.
/// </summary>
[Obsolete("Exercises the deferred retirement monitor.")]
public sealed class RetirementMonitorTests
{
    [Fact]
    public void Constructor_WithNullContainer_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new RetirementMonitor(null!));
        Assert.Equal("executionSnapshotsContainer", ex.ParamName);
    }

    [Fact]
    public void RetirementMonitor_Implements_IRetirementMonitor()
    {
        Assert.True(typeof(IRetirementMonitor).IsAssignableFrom(typeof(RetirementMonitor)));
    }

    [Fact]
    public async Task GetCandidatesAsync_Throws_NamingTheDeferral()
    {
        var monitor = new RetirementMonitor(Mock.Of<Microsoft.Azure.Cosmos.Container>());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => monitor.GetCandidatesAsync(CancellationToken.None));

        Assert.Contains("deferred at S13.223", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetirementCandidate_HasExpectedProperties()
    {
        // Verify RetirementCandidate record structure (part of public interface)
        var candidate = new RetirementCandidate
        {
            WorkflowId = "wf-1",
            Version = 1,
            LastExecutionStartedUtc = DateTime.UtcNow,
            ExecutionsInWindow = 0,
            WindowDays = 180,
            InFlightCount = 0,
            RecommendationSeverity = "safe"
        };

        Assert.Equal("wf-1", candidate.WorkflowId);
        Assert.Equal(1, candidate.Version);
        Assert.Equal(0, candidate.ExecutionsInWindow);
        Assert.Equal(180, candidate.WindowDays);
    }

    [Fact]
    public void RetirementCandidate_SupersededByVersionCanBeNull()
    {
        var candidate = new RetirementCandidate
        {
            WorkflowId = "wf-1",
            Version = 1,
            LastExecutionStartedUtc = null,
            ExecutionsInWindow = 0,
            WindowDays = 180,
            InFlightCount = 0,
            SupersededByVersion = null,
            RecommendationSeverity = "safe"
        };

        Assert.Null(candidate.SupersededByVersion);
    }

    [Fact]
    public void RetirementCandidate_RecommendationSeverityValues()
    {
        // Verify the three recommendation severity levels (Phase 2 behavior)
        var severities = new[] { "safe", "monitor", "block" };

        foreach (var severity in severities)
        {
            var candidate = new RetirementCandidate
            {
                WorkflowId = "wf-1",
                Version = 1,
                ExecutionsInWindow = 0,
                WindowDays = 180,
                InFlightCount = 0,
                RecommendationSeverity = severity
            };

            Assert.NotNull(candidate.RecommendationSeverity);
            Assert.True(severities.Contains(candidate.RecommendationSeverity));
        }
    }

    [Fact]
    public void IRetirementMonitor_HasGetCandidatesAsyncMethod()
    {
        // Verify interface contract
        var interfaceMethod = typeof(IRetirementMonitor)
            .GetMethod(nameof(IRetirementMonitor.GetCandidatesAsync));

        Assert.NotNull(interfaceMethod);
        Assert.Equal(typeof(Task<IReadOnlyList<RetirementCandidate>>), interfaceMethod!.ReturnType);
    }
}
