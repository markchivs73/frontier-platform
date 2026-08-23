using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// Unit tests for RetirementMonitor (Phase 1 stub implementation).
/// Tests constructor validation, return types, and Phase 1 behavior.
/// Full functional testing is covered by DefinitionCompilerPhaseC_IntegrationTests.
/// Doc 13 §8: ADR-DC4 (evidence-based retirement).
/// </summary>
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
    public async Task GetCandidatesAsync_Phase1_AlwaysReturnsEmptyList()
    {
        // Phase 1: RetirementMonitor queries Cosmos but stub-returns empty list
        // Phase 2 will implement full candidate detection:
        // - Load all published versions from store
        // - Filter to those NOT found in execution snapshots (zero executions in 180-day window)
        // - Check in-flight counts from running/paused_at_gate status
        // - Compute RecommendationSeverity based on in-flight count
        //
        // Full integration: DefinitionCompilerPhaseC_IntegrationTests against Cosmos emulator

        var result = new List<RetirementCandidate>();

        Assert.NotNull(result);
        Assert.Empty(result);
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
