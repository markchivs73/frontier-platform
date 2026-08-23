using Frontier.Platform.Workflow.Compiler;
using Microsoft.Extensions.AI;
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

/// <summary>
/// The entry convention a deployment declares. Names nothing real: the point is that the design
/// agent works from whatever it is told, rather than from a contract compiled into its prompt.
/// </summary>
internal sealed class SmokeEntryContract : IEntryContractCatalog
{
    public Task<EntryContractDescriptor> GetEntryContractAsync(CancellationToken ct) =>
        Task.FromResult(new EntryContractDescriptor
        {
            ContractTypeName = "CaseSummary",
            DynamicFieldName = "case_summary",
            Description = "the case summary",
        });
}

/// <summary>
/// The model client the design agent talks through. Supplying it is the consumer's job — the
/// compiler package references only Microsoft.Extensions.AI.Abstractions and never a provider.
/// </summary>
internal sealed class SmokeChatClient : IChatClient
{
    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Smoke test: never invoked.");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Smoke test: never invoked.");
}

/// <summary>Which model the design agent runs on — a deployment decision, so a port.</summary>
internal sealed class SmokeDesignerModel : IDesignerModelProvider
{
    public Task<DesignerModelSelection> GetAsync(string workflowId, CancellationToken ct) =>
        Task.FromResult(new DesignerModelSelection
        {
            ModelId = "smoke-model",
            MaxOutputTokens = 1024,
            AdaptiveThinking = false,
        });
}
