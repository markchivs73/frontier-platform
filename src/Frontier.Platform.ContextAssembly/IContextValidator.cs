
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Validates context assembly requests against a baseline catalogue (doc 04 §10, scenario 2).
/// Detects unknown component names at publish/assembly time, before invocation.
/// </summary>
public interface IContextValidator
{
    /// <summary>
    /// Validates a context request against a baseline catalogue.
    /// </summary>
    /// <param name="request">The context request with component names to validate.</param>
    /// <param name="catalogue">The baseline catalogue defining known components.</param>
    /// <returns>List of validation errors (empty if valid); each error describes an unknown component.</returns>
    IReadOnlyList<string> ValidateRequest(ContextRequest request, BaselineCatalogue catalogue);
}
