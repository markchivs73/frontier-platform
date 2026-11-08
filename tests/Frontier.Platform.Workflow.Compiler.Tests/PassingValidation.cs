using Frontier.Platform.Workflow.Model;
using Moq;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Arranges a mocked compiler whose approve-time revalidation (S13.248) passes, for tests about what an approval writes.</summary>
internal static class PassingValidation
{
    /// <summary>Makes every <see cref="IDefinitionCompiler.ValidateAsync"/> call return a clean pass for the revision asked about.</summary>
    internal static Mock<IDefinitionCompiler> PassesValidation(this Mock<IDefinitionCompiler> compiler)
    {
        compiler.Setup(c => c.ValidateAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition d, string revision, string? _, CancellationToken _) =>
                new ValidationReport(d.WorkflowId, revision, DateTime.UtcNow, ValidationOutcome.Pass, [], new Dictionary<string, string>()));
        return compiler;
    }
}
