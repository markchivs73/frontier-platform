namespace Frontier.Platform.Resilience;

/// <summary>
/// The single taxonomy every retry handler in the platform consults (doc 10 §1, §2):
/// one <see cref="Classify"/> call, one mapping table (doc 10 §3), so a permanent
/// failure short-circuits the inner Polly pipeline and the outer DTF retry loop
/// identically.
/// </summary>
public interface IFailureClassifier
{
    /// <summary>
    /// Maps <paramref name="exception"/> to its <see cref="FailureClassification"/> per
    /// doc 10 §3's table. Unmapped exception types classify
    /// <see cref="FailureClass.Permanent"/> with reason code <c>"unclassified"</c> —
    /// the fail-safe default (don't retry what you don't understand).
    /// </summary>
    FailureClassification Classify(Exception exception);
}
