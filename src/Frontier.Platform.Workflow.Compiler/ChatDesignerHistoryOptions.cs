namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The sector kill switch for design conversations (the consumer's doc 14 §2 and ADR-CD1, doc 15 §10), bound from
/// the <c>ChatDesigner</c> configuration section by
/// <see cref="CompilerServiceCollectionExtensions.AddFrontierWorkflowCompiler(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>.
/// A deployment that must not retain design conversations sets <c>ChatDesigner:HistoryEnabled</c> to
/// <see langword="false"/>: the conversation then lives only as long as the draft between publishes, and
/// approving a publish drops every turn but the welcome turn (consumer S13.294).
/// </summary>
public sealed class ChatDesignerHistoryOptions
{
    /// <summary>Whether design conversations are kept across a publish; <see langword="true"/> unless the deployment says otherwise.</summary>
    public bool HistoryEnabled { get; init; } = true;
}
