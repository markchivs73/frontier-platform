
namespace Frontier.Platform.Audit.Tests;

public sealed class WorkflowEventTypeTests
{
    [Fact]
    public void List_Always_ReturnsAllTenValuesInDeclarationOrder()
    {
        Assert.Equal(
            [
                WorkflowEventType.TaskScheduled,
                WorkflowEventType.TaskCompleted,
                WorkflowEventType.TaskFailed,
                WorkflowEventType.TaskRetried,
                WorkflowEventType.ExternalEventRaised,
                WorkflowEventType.TimerFired,
                WorkflowEventType.SubOrchestrationScheduled,
                WorkflowEventType.SubOrchestrationCompleted,
                WorkflowEventType.ExecutionStarted,
                WorkflowEventType.ExecutionCompleted,
            ],
            WorkflowEventType.List);
    }

    [Theory]
    [InlineData("task_scheduled")]
    [InlineData("task_completed")]
    [InlineData("task_failed")]
    [InlineData("task_retried")]
    [InlineData("external_event_raised")]
    [InlineData("timer_fired")]
    [InlineData("sub_orchestration_scheduled")]
    [InlineData("sub_orchestration_completed")]
    [InlineData("execution_started")]
    [InlineData("execution_completed")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, WorkflowEventType.FromName(name).Name);
    }

    [Fact]
    public void FromName_UnknownName_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowEventType.FromName("unknown"));
    }
}
