using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Hitl;
using Frontier.Platform.Resilience;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>
/// One worker process, in-process (S13.192): a generic host running a real Durable Task worker and
/// client against the emulator, with the real <see cref="GraphOrchestrator"/> and
/// <see cref="AgentTaskActivity"/> over the scripted model. The store-shaped activities record into
/// the shared <see cref="ReplayRecorder"/>. Disposing the host and building another with a new
/// <c>worker</c> label is a worker restart: the new container has no memory of the run, so
/// everything it knows it rebuilt by replaying history.
/// </summary>
internal sealed class ReplayWorkerHost : IAsyncDisposable
{
    private readonly IHost _host;

    private ReplayWorkerHost(IHost host) => _host = host;

    /// <summary>The client for scheduling, raising events and waiting on this hub.</summary>
    public DurableTaskClient Client => _host.Services.GetRequiredService<DurableTaskClient>();

    /// <summary>Builds and starts a worker labelled <paramref name="worker"/>; agent nodes in <paramref name="failingNodes"/> fail permanently, and each section's first artifact write throws <paramref name="artifactFault"/>.</summary>
    public static async Task<ReplayWorkerHost> StartAsync(string worker, ReplayRecorder recorder, IReadOnlyDictionary<string, TimeSpan>? agentDelays = null, TimeSpan? snapshotDelay = null, IReadOnlySet<string>? failingNodes = null, Exception? artifactFault = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddFrontierResilience()
            .AddSingleton<IRollbackPlanner, RollbackPlanner>()
            .AddSingleton(OrchestrationFixtures.WriteClassifier)
            .AddTransient<GraphOrchestrator>();

        var activities = new RecordingActivities(worker, recorder, snapshotDelay ?? TimeSpan.Zero, artifactFault);
        var pipeline = new ScriptedAgentTaskActivityPipeline(worker, recorder, agentDelays ?? new Dictionary<string, TimeSpan>(), failingNodes);
        builder.Services.AddDurableTaskWorker(w => ConfigureWorker(w, activities, pipeline));
        builder.Services.AddDurableTaskClient(c => c
            .Configure(options => options.DataConverter = new JsonDataConverter(CanonicalProfile.Options))
            .UseDurableTaskScheduler(DtsEmulator.ConnectionString));

        var host = builder.Build();
        await host.StartAsync();
        return new ReplayWorkerHost(host);
    }

    /// <summary>Stops the worker and drops its container — the "restart" half of a restart.</summary>
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>
    /// The registry a consumer's composition root builds, cut to the activities these walks call.
    /// <see cref="GraphOrchestrator"/> comes from DI so it gets the real resilience provider, the
    /// same way the consumer registers it.
    /// </summary>
    private static void ConfigureWorker(IDurableTaskWorkerBuilder worker, RecordingActivities activities, ScriptedAgentTaskActivityPipeline pipeline)
    {
        worker.Configure(options => options.DataConverter = new JsonDataConverter(CanonicalProfile.Options));
        worker.AddTasks(registry => registry
            .AddActivity(new AgentTaskActivity(pipeline))
            .AddActivityFunc<ExecutionSnapshot, SnapshotActivityResponse>(WorkflowActivityNames.SnapshotStateActivity, (_, snapshot) => activities.WriteSnapshotAsync(snapshot))
            .AddActivityFunc<ArtifactStateActivityRequest, ArtifactStateActivityResponse>(WorkflowActivityNames.ArtifactStateActivity, (_, request) => activities.WriteArtifact(request))
            .AddActivityFunc<GateOpenRequest, ApprovalRequest>(WorkflowActivityNames.RequestApprovalActivity, (_, request) => activities.OpenGate(request))
            .AddActivityFunc<RefreshDynamicContextRequest, DynamicContextRefreshResult>(WorkflowActivityNames.RefreshDynamicContextActivity, (_, request) => activities.Refresh(request))
            .AddActivityFunc<ConsolidateAuditInput, SignedAuditRecord>(WorkflowActivityNames.ConsolidateAuditActivity, (_, input) => activities.Consolidate(input)));
        worker.Services.AddOptions<DurableTaskRegistry>(worker.Name)
            .Configure<GraphOrchestrator>((registry, orchestrator) => registry.AddOrchestrator(orchestrator));
        worker.UseDurableTaskScheduler(DtsEmulator.ConnectionString);
    }
}
