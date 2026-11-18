using Microsoft.DurableTask;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The outer retry handler's type test, in place of <see cref="TaskFailureDetails.IsCausedBy{T}"/> (ADR-PA48, amended
/// 2026-11-17). That method resolves the failure's type name across the loaded assemblies, so its answer depends on what
/// the worker happens to have loaded — unsafe in an orchestrator body, where the handler runs — and it throws
/// <see cref="System.Reflection.AmbiguousMatchException"/> for a type-forwarded BCL type such as
/// <c>System.Net.Http.HttpRequestException</c>, which resolves through <c>netstandard</c> and <c>System.Net.Http</c>.
/// This compares the recorded full name with <typeparamref name="T"/>'s and loads nothing, so the answer is a pure
/// function of the history. It does not honour derived types: a type derived from an unsealed target
/// (<c>ContractViolationException</c>, ADR-PA34) does not match.
/// </summary>
internal static class TaskFailureCause
{
    /// <summary>Whether <paramref name="details"/>' own level recorded exactly <typeparamref name="T"/>; an empty type name never matches.</summary>
    internal static bool Is<T>(TaskFailureDetails details)
        where T : Exception =>
        !string.IsNullOrEmpty(details.ErrorType) && string.Equals(details.ErrorType, typeof(T).FullName, StringComparison.Ordinal);
}
