using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The three-way failure taxonomy every retry handler in the platform consults (doc 10
/// §1, §3, ADR-R1). <see cref="IsRetryable"/> is the single fact both the inner Polly
/// pipeline and the outer DTF retry handler need: <see cref="Permanent"/> means "never
/// retry" (the costliest mistake is burning tokens retrying a prompt that will always
/// fail); <see cref="Transient"/> and <see cref="Deferred"/> are both worth retrying,
/// the difference being whether a provider-supplied <c>Retry-After</c> dictates the
/// delay (<see cref="FailureClassification.RetryAfter"/>).
/// </summary>
public sealed class FailureClass : SmartEnum<FailureClass>
{
    /// <summary>Worth retrying with jittered backoff (e.g. provider 5xx, network blips, open circuit).</summary>
    public static readonly FailureClass Transient = new("transient", isRetryable: true);

    /// <summary>Worth retrying, but only after the provider-stated <see cref="FailureClassification.RetryAfter"/> interval (e.g. 429 with header, Cosmos 429).</summary>
    public static readonly FailureClass Deferred = new("deferred", isRetryable: true);

    /// <summary>Never retry (doc 10 §3 fail-safe default for unmapped exceptions, and the canonical classification for <see cref="ContractViolationException"/>).</summary>
    public static readonly FailureClass Permanent = new("permanent", isRetryable: false);

    private FailureClass(string name, bool isRetryable)
        : base(name)
    {
        IsRetryable = isRetryable;
    }

    /// <summary>Whether a retry handler should attempt this failure again.</summary>
    public bool IsRetryable { get; }
}
