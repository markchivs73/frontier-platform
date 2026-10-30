using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// ADR-PA40: <c>AgentTaskNode.instructions_ref</c> became optional (S4-3). A definition stored before
/// the change, which names an <c>instructions_ref</c> on every agent node, rehydrates to the bytes it
/// was stored with and hashes to the value it was published with. The expected hash was computed by
/// <c>ComputeDefinitionHash</c> over this same document before the field became nullable.
/// </summary>
public sealed class StoredDefinitionHashStabilityTests
{
    private const string StoredBeforeTheChange = """{"schema_version":"2.0","workflow_id":"wf-contracts","definition_version":2,"engagement_type":"support-triage","name":"Triage with registry contracts","nodes":[{"node_type":"mcp_tool","node_id":"get-ticket","tool_ref":"com.example.crm/tickets/get_ticket","timeout_seconds":30,"idempotency_key_spec":"engagementId","input_contract_type":"com.example.crm/get-ticket-input/1.0","output_contract_type":"com.example.crm/ticket/1.0"},{"node_type":"agent_task","node_id":"triage","role":"analyst","instructions_ref":"instructions/triage.md","input_contract_type":"acme/triage-request/1.0","output_contract_type":"acme/triage-result/1.0","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":["firm-standards","playbooks"],"dynamic_fields":["timeline","stakeholders"],"requires_real_time":true,"real_time_sources":["crm-feed"]},"tool_refs":[]}],"edges":[{"from_node_id":"get-ticket","to_node_id":"triage","kind":"data","contract_type":"com.example.crm/ticket/1.0","projection":[{"target":"ticket_id","from":"/id"},{"target":"channel","literal":"email"}]}],"definition_hash":"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","mode":"one_shot","contracts":[{"schema_version":"1.0","contract_ref":"com.example.crm/ticket/1.0","schema":{"type":"object","description":"A ticket.","properties":{"id":{"type":"string","description":"The ticket id."}},"additionalProperties":false},"rules":[{"rule":"size(self.id) != 0","message":"A ticket has an id.","field_path":"/id"}],"hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111"}]}""";

    private const string HashRecordedBeforeTheChange = "sha256:9f62a936cf75aca93683c2ac7cbb21db6f5d370bf918ff8f0506a6e8ebb4576e";

    [Fact]
    public void AStoredDefinition_RehydratesToItsStoredBytes()
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(StoredBeforeTheChange, CanonicalProfile.Options)!;

        Assert.Equal(StoredBeforeTheChange, Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(definition, CanonicalProfile.Options)));
    }

    [Fact]
    public void AStoredDefinition_HashesToTheValueItWasPublishedWith()
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(StoredBeforeTheChange, CanonicalProfile.Options)!;

        Assert.Equal(HashRecordedBeforeTheChange, new DefinitionValidator([]).ComputeDefinitionHash(definition));
    }
}
