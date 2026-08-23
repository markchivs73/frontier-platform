using Frontier.Platform.Workflow.Compiler;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.SmokeConsumer;

/// <summary>
/// Every catalogue port the compiler requires a consumer to answer, in one class. They return
/// nothing: what is being proved is that a consumer *can* implement them from outside the
/// assembly — which packing, unit tests and architecture tests inside the producing repo all
/// pass without checking.
/// </summary>
internal sealed class SmokeCatalogues :
    IAgentRoleCatalog,
    IApproverRoleCatalog,
    IInstructionCatalog,
    IContextComponentCatalog,
    IRetryProfileCatalog,
    IDesignerToolCatalog,
    ICascadeGraphChecker
{
    public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>([]);

    public Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApproverRoleDescriptor>>([]);

    public Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct) => Task.FromResult(false);

    public Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task<IReadOnlyList<RetryProfileDescriptor>> GetProfilesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RetryProfileDescriptor>>([]);

    public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>([]);

    public IReadOnlyList<string> CheckAtPublish(WorkflowDefinition definition) => [];
}
