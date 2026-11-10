using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S13.138: a version published before publish stamped the hash gets the recorded hash on read.</summary>
public sealed class RecordedDefinitionHashTests
{
    private static DefinitionVersionDocument Version(string inlineHash) => new()
    {
        Id = "wf-1:v1", WorkflowId = "wf-1", State = "published", DefinitionVersion = 1, DefinitionHash = "sha256:recorded",
        Definition = new WorkflowDefinition
        {
            WorkflowId = "wf-1", DefinitionVersion = 1, EngagementType = "t", Name = "n", Nodes = [], Edges = [],
            DefinitionHash = inlineHash, Mode = ExecutionMode.OneShot,
        },
        ProposedBy = "user:p", ApprovedBy = "user:a", ProposedUtc = DateTime.UnixEpoch, ApprovedUtc = DateTime.UnixEpoch,
        ValidationReportRef = "report-1",
    };

    [Fact]
    public void Fill_GivesAnEmptyInlineHashTheVersionsRecordedHash()
    {
        Assert.Equal("sha256:recorded", RecordedDefinitionHash.Fill(Version(string.Empty)).Definition.DefinitionHash);
    }

    [Fact]
    public void Fill_LeavesADefinitionThatNamesAHashAsStored()
    {
        var version = Version("sha256:stamped");

        Assert.Same(version, RecordedDefinitionHash.Fill(version));
    }

    [Fact]
    public void Fill_LeavesTheVersionNumberAVersionWasStoredWith()
    {
        // S13.273: versions published through v0.56.0 carry the draft's number (often 0) inside;
        // a read never re-stamps it, so the stored definition and its recorded hash still agree.
        var stored = Version(string.Empty);
        stored = stored with { Definition = stored.Definition with { DefinitionVersion = 0 } };

        var read = RecordedDefinitionHash.Fill(stored);

        Assert.Equal(0, read.Definition.DefinitionVersion);
        Assert.Equal(1, read.DefinitionVersion);
        Assert.Equal(stored.Definition with { DefinitionHash = "sha256:recorded" }, read.Definition);
    }

    [Fact]
    public void Fill_RefusesANullVersion()
    {
        Assert.Throws<ArgumentNullException>(() => RecordedDefinitionHash.Fill(null!));
    }
}
