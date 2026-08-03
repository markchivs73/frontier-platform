namespace Frontier.Platform.Observability;

/// <summary>
/// Pure stateless maturity band evaluator (doc 11 §6): computes the new
/// <see cref="MaturityAssessment"/> for one (agent_role × engagement_type) pair given
/// fresh statistics and the previous assessment (for hysteresis). No side effects — the
/// caller persists the result.
/// </summary>
internal static class MaturityEvaluator
{
    /// <summary>
    /// Evaluates the maturity band for <paramref name="agentRole"/> ×
    /// <paramref name="engagementType"/> from fresh statistics, applying the two-window
    /// hysteresis rule: a band change requires the candidate level to hold for two
    /// consecutive evaluation windows (doc 11 §6).
    /// </summary>
    internal static MaturityAssessment Evaluate(
        string agentRole,
        string engagementType,
        int sampleSize,
        DateRange window,
        decimal validatorPassRate,
        decimal hitlRejectionRate,
        decimal overrideRate,
        MaturityThresholds thresholds,
        MaturityAssessment? previous)
    {
        if (sampleSize < thresholds.MinimumSample)
            return BuildAssessment(agentRole, engagementType, sampleSize, window, validatorPassRate, hitlRejectionRate, overrideRate, band: null, pendingTransition: null);

        var candidate = ComputeCandidate(validatorPassRate, hitlRejectionRate, thresholds);
        var currentBand = previous?.Band ?? MaturityBand.Provisional;

        if (candidate == currentBand)
            return BuildAssessment(agentRole, engagementType, sampleSize, window, validatorPassRate, hitlRejectionRate, overrideRate, currentBand, pendingTransition: null);

        var (newBand, newPending) = ApplyHysteresis(candidate, currentBand, previous?.PendingTransition);
        return BuildAssessment(agentRole, engagementType, sampleSize, window, validatorPassRate, hitlRejectionRate, overrideRate, newBand, newPending);
    }

    internal static MaturityBand ComputeCandidate(decimal passRate, decimal rejectionRate, MaturityThresholds t)
    {
        if (passRate >= t.TrustedPassRate && rejectionRate <= t.TrustedRejectionRate)
            return MaturityBand.Trusted;

        if (passRate >= t.CalibratedPassRate && rejectionRate <= t.CalibratedRejectionRate)
            return MaturityBand.Calibrated;

        return MaturityBand.Provisional;
    }

    internal static (MaturityBand Band, MaturityBand? Pending) ApplyHysteresis(
        MaturityBand candidate, MaturityBand currentBand, MaturityBand? pendingTransition)
    {
        if (pendingTransition == candidate)
            return (candidate, null);

        return (currentBand, candidate);
    }

    internal static MaturityAssessment BuildAssessment(
        string agentRole, string engagementType, int sampleSize, DateRange window,
        decimal passRate, decimal rejectionRate, decimal overrideRate,
        MaturityBand? band, MaturityBand? pendingTransition) =>
        new(agentRole, engagementType, band, sampleSize, window,
            passRate, rejectionRate, overrideRate, pendingTransition,
            EvidenceQueryRef: $"audit-records?agentRole={agentRole}&engagementType={engagementType}&from={window.From:O}&to={window.To:O}");
}
