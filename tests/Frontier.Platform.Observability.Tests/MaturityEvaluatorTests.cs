namespace Frontier.Platform.Observability.Tests;

/// <summary>S6.8 tests for <see cref="MaturityEvaluator"/> (doc 11 §6).</summary>
public sealed class MaturityEvaluatorTests
{
    private static readonly MaturityThresholds DefaultThresholds = MaturityThresholds.Default;
    private static readonly DateRange Window = new(DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow);

    [Fact]
    public void Evaluate_BelowMinimumSample_ReturnsNullBand()
    {
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 5, Window,
            validatorPassRate: 0.95m, hitlRejectionRate: 0.01m, overrideRate: 0m,
            DefaultThresholds, previous: null);

        Assert.Null(result.Band);
        Assert.Null(result.PendingTransition);
        Assert.Equal(5, result.SampleSize);
    }

    [Fact]
    public void Evaluate_AtMinimumSample_EvaluatesBand()
    {
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 20, Window,
            validatorPassRate: 0.95m, hitlRejectionRate: 0.01m, overrideRate: 0m,
            DefaultThresholds, previous: null);

        Assert.NotNull(result.Band);
    }

    [Theory]
    [InlineData(0.95, 0.03, MaturityBand.Trusted)]
    [InlineData(0.90, 0.05, MaturityBand.Trusted)]
    [InlineData(0.80, 0.10, MaturityBand.Calibrated)]
    [InlineData(0.75, 0.15, MaturityBand.Calibrated)]
    [InlineData(0.74, 0.10, MaturityBand.Provisional)]
    [InlineData(0.80, 0.16, MaturityBand.Provisional)]
    [InlineData(0.50, 0.50, MaturityBand.Provisional)]
    public void ComputeCandidate_VariousRates_ReturnsExpectedBand(
        double passRate, double rejectionRate, MaturityBand expected)
    {
        var result = MaturityEvaluator.ComputeCandidate((decimal)passRate, (decimal)rejectionRate, DefaultThresholds);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_CandidateSameAsCurrentBand_NoPendingTransition()
    {
        var previous = BuildAssessment(MaturityBand.Calibrated, pendingTransition: null);

        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 50, Window,
            validatorPassRate: 0.80m, hitlRejectionRate: 0.10m, overrideRate: 0m,
            DefaultThresholds, previous);

        Assert.Equal(MaturityBand.Calibrated, result.Band);
        Assert.Null(result.PendingTransition);
    }

    [Fact]
    public void Evaluate_CandidateDiffersFromCurrent_SetsPendingTransition()
    {
        var previous = BuildAssessment(MaturityBand.Provisional, pendingTransition: null);

        // Pass rate qualifies for Calibrated
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 50, Window,
            validatorPassRate: 0.80m, hitlRejectionRate: 0.10m, overrideRate: 0m,
            DefaultThresholds, previous);

        // Band does NOT change yet (only first qualifying window)
        Assert.Equal(MaturityBand.Provisional, result.Band);
        Assert.Equal(MaturityBand.Calibrated, result.PendingTransition);
    }

    [Fact]
    public void Evaluate_SecondConsecutiveQualifyingWindow_ChangesBand()
    {
        var previous = BuildAssessment(MaturityBand.Provisional, pendingTransition: MaturityBand.Calibrated);

        // Second consecutive window qualifying for Calibrated
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 50, Window,
            validatorPassRate: 0.80m, hitlRejectionRate: 0.10m, overrideRate: 0m,
            DefaultThresholds, previous);

        Assert.Equal(MaturityBand.Calibrated, result.Band);
        Assert.Null(result.PendingTransition);
    }

    [Fact]
    public void Evaluate_PendingTransitionResetsIfCandidateChanges()
    {
        var previous = BuildAssessment(MaturityBand.Provisional, pendingTransition: MaturityBand.Trusted);

        // Candidate drops back to Calibrated (not the pending Trusted)
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 50, Window,
            validatorPassRate: 0.80m, hitlRejectionRate: 0.10m, overrideRate: 0m,
            DefaultThresholds, previous);

        // Still Provisional; pending resets to Calibrated (not Trusted)
        Assert.Equal(MaturityBand.Provisional, result.Band);
        Assert.Equal(MaturityBand.Calibrated, result.PendingTransition);
    }

    [Fact]
    public void Evaluate_PopulatesAgentRoleEngagementType()
    {
        var result = MaturityEvaluator.Evaluate(
            "pricing-agent", "advisory-sow", sampleSize: 30, Window,
            0.80m, 0.10m, 0m, DefaultThresholds, previous: null);

        Assert.Equal("pricing-agent", result.AgentRole);
        Assert.Equal("advisory-sow", result.EngagementType);
    }

    [Fact]
    public void Evaluate_PopulatesNonEmptyEvidenceQueryRef()
    {
        var result = MaturityEvaluator.Evaluate(
            "writer", "advisory-sow", sampleSize: 30, Window,
            0.80m, 0.10m, 0m, DefaultThresholds, previous: null);

        Assert.False(string.IsNullOrEmpty(result.EvidenceQueryRef));
        Assert.Contains("writer", result.EvidenceQueryRef, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyHysteresis_PendingMatchesCandidate_ReturnsCandidateWithNullPending()
    {
        var (band, pending) = MaturityEvaluator.ApplyHysteresis(
            MaturityBand.Trusted, MaturityBand.Calibrated, MaturityBand.Trusted);

        Assert.Equal(MaturityBand.Trusted, band);
        Assert.Null(pending);
    }

    [Fact]
    public void ApplyHysteresis_NoPriorPending_KeepsCurrentBandSetsPending()
    {
        var (band, pending) = MaturityEvaluator.ApplyHysteresis(
            MaturityBand.Trusted, MaturityBand.Calibrated, pendingTransition: null);

        Assert.Equal(MaturityBand.Calibrated, band);
        Assert.Equal(MaturityBand.Trusted, pending);
    }

    private static MaturityAssessment BuildAssessment(MaturityBand band, MaturityBand? pendingTransition) =>
        new("writer", "advisory-sow", band, 50, Window, 0.80m, 0.10m, 0m, pendingTransition, "ref");
}
