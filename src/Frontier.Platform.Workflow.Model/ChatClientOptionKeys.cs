namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Well-known keys for <c>ChatOptions.AdditionalProperties</c> entries that cross the
/// library boundary between a chat-client consumer and the provider adapter that
/// interprets them (S9.9a). Lives here — like <see cref="WorkflowActivityNames"/> — because
/// the consumer (Definition Compiler) and the adapter (Orchestration's
/// <c>AnthropicChatClient</c>) may not reference each other directly (library-boundaries
/// skill), yet the key string is a contract between them: two privately-defined copies
/// would drift silently.
/// </summary>
public static class ChatClientOptionKeys
{
    /// <summary>
    /// Boolean flag requesting the provider's adaptive/extended-thinking mode (doc 14 §3:
    /// higher proposal quality on complex graph edits). Provider-neutral by design — the
    /// consumer states intent; the adapter owns the provider-specific request shape.
    /// Adapters without a thinking concept ignore it.
    /// </summary>
    public const string AdaptiveThinking = "adaptive_thinking";
}
