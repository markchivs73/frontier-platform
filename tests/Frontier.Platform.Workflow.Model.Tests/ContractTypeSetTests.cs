using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// S13.12c: the composition-root-supplied contract set (E16 option 2). The construction-time
/// validation is the point — a mis-registration must fail at boot, not at the first design
/// turn that happens to name the bad contract.
///
/// The fixtures are the engine's own contract types. They were this workload's contracts until
/// the model moved here (E3b), which is exactly the coupling the move exists to remove: a
/// package cannot reach for its consumer's vocabulary, not even in a test.
/// </summary>
public sealed class ContractTypeSetTests
{
    private abstract class AbstractContract : IVersionedContract
    {
        public string SchemaVersion => "1.0";

        public void Validate()
        {
        }
    }

    private sealed class NotAContract;

    [Fact]
    public void Constructor_NullTypes_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ContractTypeSet(null!));

    [Fact]
    public void Constructor_TypeThatIsNotAContract_ThrowsNamingTheOffender()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new ContractTypeSet([typeof(WorkflowDefinition), typeof(NotAContract)]));

        Assert.Contains(nameof(NotAContract), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(WorkflowDefinition), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AbstractContract_Throws() =>
        Assert.Throws<ArgumentException>(() => new ContractTypeSet([typeof(AbstractContract)]));

    [Fact]
    public void Constructor_ContractInterface_Throws() =>
        Assert.Throws<ArgumentException>(() => new ContractTypeSet([typeof(IVersionedContract)]));

    [Fact]
    public void Names_Always_AreOrdinallySorted()
    {
        var set = new ContractTypeSet([typeof(WorkflowDefinition), typeof(PayloadRef), typeof(ExecutionSnapshot)]);

        Assert.Equal([nameof(ExecutionSnapshot), nameof(PayloadRef), nameof(WorkflowDefinition)], set.Names);
    }

    [Fact]
    public void Resolve_RegisteredName_ReturnsTheType()
    {
        var set = new ContractTypeSet([typeof(WorkflowDefinition)]);

        Assert.Equal(typeof(WorkflowDefinition), set.Resolve(nameof(WorkflowDefinition)));
    }

    [Fact]
    public void Resolve_UnregisteredName_ReturnsNull()
    {
        var set = new ContractTypeSet([typeof(WorkflowDefinition)]);

        Assert.Null(set.Resolve("NoSuchContract"));
    }

    [Fact]
    public void Resolve_IsCaseSensitive_BecauseContractNamesAreOrdinal()
    {
        var set = new ContractTypeSet([typeof(WorkflowDefinition)]);

        Assert.Null(set.Resolve("workflowdefinition"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingName_Throws(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new ContractTypeSet([]).Resolve(name!));

    [Fact]
    public void Constructor_EmptySet_IsLegal() =>
        Assert.Empty(new ContractTypeSet([]).Names);
}
