using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The outcome of a Check agent's validation run, recorded on a
/// <see cref="ValidatorOutcome"/> (doc 05 §3). Serializes as a snake_case string,
/// identical to a standard enum (doc 00 §3.5). No producer exists before Stage 6;
/// <see cref="SignedAuditRecord.ValidatorOutcomes"/> is <c>[]</c> until then.
/// </summary>
public sealed class ValidatorStatus : SmartEnum<ValidatorStatus>
{
    /// <summary>The validator found no issues.</summary>
    public static readonly ValidatorStatus Pass = new("pass");

    /// <summary>The validator found a blocking issue.</summary>
    public static readonly ValidatorStatus Fail = new("fail");

    /// <summary>The validator found a non-blocking issue.</summary>
    public static readonly ValidatorStatus Warn = new("warn");

    private ValidatorStatus(string name)
        : base(name)
    {
    }
}
