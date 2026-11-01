using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The Cosmos database the compiler's stores read and write, bound from the <c>Cosmos</c> configuration
/// section by <see cref="CompilerServiceCollectionExtensions.AddFrontierWorkflowCompiler(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>,
/// as the other platform libraries bind theirs. The compiler resolves its <c>CosmosClient</c> from the
/// container, so only the database name is its own. The default matches the local emulator seeded by
/// the consumer's dev setup. Before v0.48.0 the name was hard-coded, so a deployment or test that set
/// <c>Cosmos:Database</c> still had its definitions read from, and written to, <c>frontier-workflow</c>.
/// </summary>
public sealed class CosmosOptions
{
    /// <summary>The database holding <c>workflow-definitions</c> and <c>execution-snapshots</c>.</summary>
    [Required]
    public string Database { get; init; } = "frontier-workflow";
}
