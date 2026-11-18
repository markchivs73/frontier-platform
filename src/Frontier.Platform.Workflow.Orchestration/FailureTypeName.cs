using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The orchestrator body's failure-type test, in place of <see cref="TaskFailureDetails.IsCausedBy{T}"/> (ADR-PA48, amended
/// 2026-11-17): an exact, ordinal match of the recorded type name, loading nothing. <c>IsCausedBy</c> resolves the name
/// across the loaded assemblies — an answer that depends on the worker, in code that must replay identically — and
/// throws <see cref="System.Reflection.AmbiguousMatchException"/> for a type-forwarded type such as
/// <c>HttpRequestException</c>, which faulted the run before its failed checkpoint and audit record were written. It is
/// the rule Resilience's outer retry handler applies; a type derived from an unsealed target does not match.
/// </summary>
internal static class FailureTypeName
{
    /// <summary>Whether <paramref name="details"/>' own level recorded exactly <typeparamref name="T"/>; an empty type name never matches.</summary>
    internal static bool Is<T>(TaskFailureDetails details)
        where T : Exception =>
        !string.IsNullOrEmpty(details.ErrorType) && string.Equals(details.ErrorType, typeof(T).FullName, StringComparison.Ordinal);
}
