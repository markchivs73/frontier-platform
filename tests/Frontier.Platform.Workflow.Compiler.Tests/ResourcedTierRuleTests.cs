#pragma warning disable CA1034, CA1515
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// Resourced-tier validation rule coverage (doc 13 §4.2, S9.27c — the first two rules pulled
/// ahead of the full S9.30 rollout, C-21b). Each rule tested with positive and negative cases,
/// mirroring <see cref="ValidationRulesTests"/>'s pure-tier convention.
/// </summary>
public sealed class ResourcedTierRuleTests
{
    public sealed class McpToolResolvesRuleTests
    {
        [Fact]
        public async Task AllToolRefsKnown_ReturnsEmpty()
        {
            var rule = new McpToolResolvesRule(new FakeToolCatalog("io.frontier.demo/autotask/get_new_ticket"));
            var ctx = new DefinitionValidationContext(DefinitionWithAgentTask(toolRefs: ["io.frontier.demo/autotask/get_new_ticket"]));

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task UnknownToolRef_ReturnsFindingWithNodeIdAndRuleId()
        {
            var rule = new McpToolResolvesRule(new FakeToolCatalog("io.frontier.demo/autotask/get_new_ticket"));
            var ctx = new DefinitionValidationContext(DefinitionWithAgentTask(toolRefs: ["connectors/autotask-demo.invented_tool"]));

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            var finding = Assert.Single(findings);
            Assert.Equal("mcp.tool-resolves", finding.RuleId);
            Assert.Equal("agent-1", finding.NodeId);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
        }

        [Fact]
        public async Task NoToolRefs_ReturnsEmpty()
        {
            var rule = new McpToolResolvesRule(new FakeToolCatalog());
            var ctx = new DefinitionValidationContext(DefinitionWithAgentTask(toolRefs: []));

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task NullContext_Throws()
        {
            var rule = new McpToolResolvesRule(new FakeToolCatalog());

            await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, CancellationToken.None));
        }

        [Fact]
        public void NullToolCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new McpToolResolvesRule(null!));
    }

    public sealed class ModelRoleRolesExistRuleTests
    {
        [Fact]
        public async Task RoleExists_ReturnsEmpty()
        {
            var rule = new ModelRoleRolesExistRule(new FakeAgentRoleCatalog("deep-reasoning"));
            var ctx = new DefinitionValidationContext(DefinitionWithAgentTask(role: "deep-reasoning"));

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task UnknownRole_ReturnsFindingWithNodeIdAndRuleId()
        {
            var rule = new ModelRoleRolesExistRule(new FakeAgentRoleCatalog("deep-reasoning"));
            var ctx = new DefinitionValidationContext(DefinitionWithAgentTask(role: "helpdesk-coordinator"));

            var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

            var finding = Assert.Single(findings);
            Assert.Equal("model-role.roles-exist", finding.RuleId);
            Assert.Equal("agent-1", finding.NodeId);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
        }

        [Fact]
        public async Task NullContext_Throws()
        {
            var rule = new ModelRoleRolesExistRule(new FakeAgentRoleCatalog());

            await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, CancellationToken.None));
        }

        [Fact]
        public void NullAgentRoleCatalog_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new ModelRoleRolesExistRule(null!));
    }

    private static WorkflowDefinition DefinitionWithAgentTask(
        IReadOnlyList<string>? toolRefs = null, string role = "deep-reasoning") => new()
    {
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Test Workflow",
        Nodes =
        [
            new AgentTaskNode
            {
                NodeId = "agent-1",
                Role = role,
                InstructionsRef = "scope-gen",
                InputContractType = "BriefArtifact",
                OutputContractType = "SummaryArtifact",
                ToolRefs = toolRefs ?? [],
                ContextRequest = new ContextRequest
                {
                    EngagementId = "engagement-id",
                    AgentRole = "test-role",
                    BaselineComponents = [],
                    DynamicFields = [],
                },
            },
        ],
        Edges = [],
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    private sealed class FakeToolCatalog(params string[] toolRefs) : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>(
            [
                .. toolRefs.Select(toolRef => new DesignerToolDescriptor
                {
                    ToolRef = toolRef,
                    Server = toolRef[..Math.Max(toolRef.LastIndexOf('/'), 0)],
                    Name = toolRef.Split('/').Last(),
                    Description = "fake tool",
                }),
            ]);
    }

    private sealed class FakeAgentRoleCatalog(params string[] roleIds) : IAgentRoleCatalog
    {
        public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>(
            [
                .. roleIds.Select(roleId => new AgentRoleDescriptor { RoleId = roleId, Description = "fake role" }),
            ]);
    }
}
