
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Which node types the deployment's orchestrator can actually execute (ADR-DC7, S13.7h).
///
/// A consumer-owned seam, like <see cref="IContractTypeCatalog"/>: the compiler must not reference
/// Orchestration (library-boundaries), so the Host composition root adapts the runtime's own
/// capability list onto this port. It backs both the <c>structure.node-type-supported</c> rule and
/// the designer schema's <c>executable</c> flag, so a node type is offered to the design agent if
/// and only if the runtime will run it.
/// </summary>
public interface IExecutableNodeTypeCatalog
{
    /// <summary>Whether the orchestrator can execute <paramref name="nodeType"/>.</summary>
    bool IsExecutable(NodeType nodeType);

    /// <summary>Executable node-type wire names, sorted ordinally — for error messages and the schema.</summary>
    IReadOnlyList<string> ExecutableNodeTypeNames { get; }
}

/// <summary>
/// Fallback <see cref="IExecutableNodeTypeCatalog"/> for hosts that compose the compiler without a
/// runtime (tests, the schema-only path): every declared node type is treated as executable, which
/// preserves pre-S13.7h behaviour rather than silently rejecting definitions a real runtime might
/// well support. The Host replaces it with the orchestrator-backed adapter.
/// </summary>
// Retained for tests and for a consumer that deliberately wants validation decoupled from any
// runtime. It is no longer the default: see OrchestratorExecutableNodeTypeCatalog below.
public sealed class PermissiveExecutableNodeTypeCatalog : IExecutableNodeTypeCatalog
{
    /// <inheritdoc />
    public bool IsExecutable(NodeType nodeType) => true;

    /// <inheritdoc />
    public IReadOnlyList<string> ExecutableNodeTypeNames { get; } =
        [.. NodeType.List.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal)];
}

/// <summary>
/// The default <see cref="IExecutableNodeTypeCatalog"/>: what the interpreter in this same
/// release actually executes, read from <see cref="OrchestratorCapabilities"/>.
///
/// <para>The default used to be <see cref="PermissiveExecutableNodeTypeCatalog"/> — everything
/// validates — because the compiler could not see the interpreter's capabilities when the two
/// lived in separate solutions. It can now, so the safe answer is the default rather than
/// something a consumer has to remember to register. That matters because the failure mode was
/// silent and fail-**open**: a missed registration meant the compiler happily published
/// workflows the runtime would reject at execution, which is the S13.7h hole reopening without
/// anything going red.</para>
///
/// <para>A deployment whose runtime executes a different set still overrides this — the port
/// exists for exactly that — but it now overrides a correct default rather than supplying the
/// only correct answer.</para>
/// </summary>
public sealed class OrchestratorExecutableNodeTypeCatalog : IExecutableNodeTypeCatalog
{
    /// <inheritdoc />
    public bool IsExecutable(NodeType nodeType) => OrchestratorCapabilities.Supports(nodeType);

    /// <inheritdoc />
    public IReadOnlyList<string> ExecutableNodeTypeNames { get; } =
        [.. OrchestratorCapabilities.SupportedNodeTypes.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal)];
}
