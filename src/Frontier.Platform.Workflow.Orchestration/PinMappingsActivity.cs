using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Pins the served model-role mapping version for every role an execution uses (doc 08 §5,
/// ADR-PA29): <see cref="GraphOrchestrator"/>'s first action when
/// <see cref="GraphOrchestratorInput.PinModelRoles"/> is set. The result lands in history, so a
/// later remap or rollback cannot reach a running execution, and replay sees the same pins.
/// <para>
/// The result is a list ordered by role id, never a dictionary, so its recorded bytes are
/// canonical. An unmapped role fails permanently here, before any agent runs.
/// <see cref="TaskActivityContext"/> carries no <see cref="CancellationToken"/>;
/// <see cref="CancellationToken.None"/> is this codebase's DTF-activity convention.
/// </para>
/// </summary>
[DurableTask(WorkflowActivityNames.PinMappingsActivity)]
public sealed class PinMappingsActivity : TaskActivity<PinMappingsRequest, IReadOnlyList<ModelRolePin>>
{
    private readonly IMappingPinner pinner;

    /// <summary>Constructs the activity over the Model-Role Config pinner.</summary>
    public PinMappingsActivity(IMappingPinner pinner)
    {
        ArgumentNullException.ThrowIfNull(pinner);
        this.pinner = pinner;
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<ModelRolePin>> RunAsync(TaskActivityContext context, PinMappingsRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return pinner.PinAsync(input.EngagementId, input.RoleIds, CancellationToken.None);
    }
}
