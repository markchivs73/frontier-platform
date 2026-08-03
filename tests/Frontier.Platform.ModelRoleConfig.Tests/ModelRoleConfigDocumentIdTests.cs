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
}
