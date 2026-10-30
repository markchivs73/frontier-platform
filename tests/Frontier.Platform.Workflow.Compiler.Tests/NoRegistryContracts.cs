using Moq;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>The real embedder over an empty registry: what a definition naming only CLR contracts publishes with.</summary>
internal static class NoRegistryContracts
{
    internal static IDefinitionContractEmbedder Embedder { get; } = new DefinitionContractEmbedder(Mock.Of<IContractSnapshotCatalog>());
}
