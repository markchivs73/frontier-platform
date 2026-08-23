using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Orchestration library configuration, bound from the <c>Orchestration</c> configuration
/// section (S4.2).
/// </summary>
public sealed class OrchestrationOptions
{
    /// <summary>The configuration section name this type binds to.</summary>
    public const string ArtifactName = "Orchestration";

    /// <summary>
    /// The directory <see cref="FileInstructionsResolver"/> resolves
    /// <see cref="Abstractions.AgentTaskNode.InstructionsRef"/> paths against (e.g. the repo's
    /// <c>instructions/</c> folder is referenced as <c>instructions/gen-scope.md</c> relative
    /// to this root). PoC-grade file resolver (doc 14, Stage 8, placeholder per S4.1).
    /// </summary>
    [Required]
    public string InstructionsRootPath { get; set; } = AppContext.BaseDirectory;
}
