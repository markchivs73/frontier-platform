namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="ModelRoleConfigDocumentId"/>'s deterministic id formatting (doc 08 §6).</summary>
public sealed class ModelRoleConfigDocumentIdTests
{
    [Fact]
    public void ForVersion_FormatsRoleIdAndMappingVersion()
    {
        var id = ModelRoleConfigDocumentId.ForVersion("deep-reasoning", 1);

        Assert.Equal("deep-reasoning:v1", id);
    }

    [Fact]
    public void ForCurrent_FormatsRoleId()
    {
        var id = ModelRoleConfigDocumentId.ForCurrent("deep-reasoning");

        Assert.Equal("deep-reasoning:current", id);
    }

    [Fact]
    public void ForDefinitionVersion_FormatsUnderTheDefinitionInfix()
    {
        Assert.Equal("deep-reasoning:definition:v3", ModelRoleConfigDocumentId.ForDefinitionVersion("deep-reasoning", 3));
    }

    [Fact]
    public void ForDefinitionCurrent_FormatsUnderTheDefinitionInfix()
    {
        Assert.Equal("deep-reasoning:definition:current", ModelRoleConfigDocumentId.ForDefinitionCurrent("deep-reasoning"));
    }
}
