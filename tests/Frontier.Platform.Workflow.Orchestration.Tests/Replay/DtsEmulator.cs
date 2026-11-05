namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>
/// Where the replay suite finds the Durable Task Scheduler emulator (S13.192). CI starts one on
/// <c>http://localhost:8080</c> serving the <c>frontier-platform-replay</c> hub; both values are
/// env-overridable for a local run.
/// <para>
/// The default hub is deliberately <b>not</b> the consumer dev stack's <c>frontier</c> hub. DTS hands
/// an orchestration's work to any worker connected to its hub, so a dev Host running against that hub
/// could pick up this suite's runs and call its real activities, model included. Point
/// <c>DTS_EMULATOR_TASKHUB</c> at <c>frontier</c> only when no other worker is attached to it. The
/// emulator serves only the hubs it was started with (<c>Database__TaskHubNames__0</c>) and never
/// creates one on demand.
/// </para>
/// </summary>
internal static class DtsEmulator
{
    /// <summary>The emulator's gRPC endpoint; <c>DTS_EMULATOR_ENDPOINT</c> overrides.</summary>
    internal static string Endpoint =>
        Environment.GetEnvironmentVariable("DTS_EMULATOR_ENDPOINT") ?? "http://localhost:8080";

    /// <summary>The task hub; <c>DTS_EMULATOR_TASKHUB</c> overrides.</summary>
    internal static string TaskHub =>
        Environment.GetEnvironmentVariable("DTS_EMULATOR_TASKHUB") ?? "frontier-platform-replay";

    /// <summary>The scheduler connection string the worker and client both use. The emulator takes no credential.</summary>
    internal static string ConnectionString => $"Endpoint={Endpoint};Authentication=None;TaskHub={TaskHub}";
}
