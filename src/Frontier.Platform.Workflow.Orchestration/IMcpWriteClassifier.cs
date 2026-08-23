namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Answers whether an MCP tool mutates its connector's state, so the interpreter can fence
/// writes when an execution runs in sandbox mode.
///
/// This is a **consumer-owned port** because the answer is deployment knowledge, not engine
/// knowledge: it depends on which connectors a deployment has wired and what their tools do.
/// The engine asks; it must never hold the list. Before E3b step 3 it did hold the list — a
/// hardcoded set of two demo connectors' tool names, reachable from the orchestrator body —
/// which no type-based architecture test could see, because the coupling lived in string
/// literals rather than in a signature.
///
/// <para><b>Implementations must be pure.</b> This is called from inside the orchestrator body,
/// where DTF replays every decision. An implementation must be a total function of its
/// argument: no I/O, no clock, no configuration read at call time, and the same answer for the
/// same tool reference on every replay for the lifetime of an execution. The same requirement
/// applies to <see cref="Frontier.Platform.Resilience.IResiliencePolicyProvider"/>, which is
/// constructor-injected into the orchestrator for the same reason (dtf-determinism).</para>
///
/// <para>A deployment that cannot classify a tool should answer <see langword="true"/>: fencing
/// a read is a wasted call, while letting an unclassified write through defeats the sandbox.</para>
/// </summary>
public interface IMcpWriteClassifier
{
    /// <summary>Whether <paramref name="toolRef"/> mutates its connector's state.</summary>
    bool IsWrite(McpToolRef toolRef);
}
