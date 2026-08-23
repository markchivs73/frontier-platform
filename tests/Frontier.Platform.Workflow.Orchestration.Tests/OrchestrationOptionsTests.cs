namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="OrchestrationOptions"/>.</summary>
public sealed class OrchestrationOptionsTests
{
    [Fact]
    public void InstructionsRootPath_DefaultsToAppContextBaseDirectory()
    {
        var options = new OrchestrationOptions();

        Assert.Equal(AppContext.BaseDirectory, options.InstructionsRootPath);
    }

    [Fact]
    public void InstructionsRootPath_CanBeOverridden()
    {
        var options = new OrchestrationOptions { InstructionsRootPath = "/custom/instructions" };

        Assert.Equal("/custom/instructions", options.InstructionsRootPath);
    }
}
