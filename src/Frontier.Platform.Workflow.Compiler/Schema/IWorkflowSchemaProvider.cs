using Frontier.Platform.Workflow.Model;
namespace Frontier.Platform.Workflow.Compiler.Schema;

/// <summary>
/// Supplies the workflow design-language schema (doc 14 §7). The schema is generated once from
/// the loaded Abstractions assembly and cached for the process lifetime — it is stable per
/// deployment (the types it describes only change by deployment), and the chat designer pins it
/// into the agent prompt once per session.
/// </summary>
public interface IWorkflowSchemaProvider
{
    /// <summary>Returns the cached workflow schema, generating it on first access.</summary>
    WorkflowSchema GetSchema();
}

/// <summary>
/// Caching <see cref="IWorkflowSchemaProvider"/>: generates the schema lazily on first access and
/// holds it for the process lifetime. Registered as a singleton (S9.7).
/// </summary>
public sealed class WorkflowSchemaProvider : IWorkflowSchemaProvider
{
    private readonly Lazy<WorkflowSchema> _schema;

    /// <summary>Creates a provider over the deployment's executable node types and workload contract set (S13.12c).</summary>
    public WorkflowSchemaProvider(IExecutableNodeTypeCatalog executable, IContractTypeSet contractTypes)
        // The summaries are read from the model *package* now (ADR-PA3). NuGet does not deploy a
        // package's XML docs by default, so this project sets CopyDocumentationFilesFromPackages;
        // without it the file is simply absent and every description silently becomes empty.
        : this(new WorkflowSchemaGenerator(XmlDocReader.ForAssembly(typeof(WorkflowNode).Assembly), executable, contractTypes))
    {
    }

    /// <summary>Creates a provider over a specific generator (test seam).</summary>
    internal WorkflowSchemaProvider(WorkflowSchemaGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _schema = new Lazy<WorkflowSchema>(generator.Generate);
    }

    /// <inheritdoc />
    public WorkflowSchema GetSchema() => _schema.Value;
}
