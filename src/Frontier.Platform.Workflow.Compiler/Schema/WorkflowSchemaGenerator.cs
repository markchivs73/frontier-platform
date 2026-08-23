using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Schema;

/// <summary>
/// Generates the workflow design-language schema by reflecting over
/// <c>Frontier.Reason.Workflow.Abstractions</c> (doc 14 §7, ADR-CD3). Node types are
/// discovered from <see cref="WorkflowNode"/>'s polymorphic discriminators; fields from the
/// nodes' canonical JSON properties; enum value lists from the domain smart enums. The worker
/// executes these types, the compiler validates them, and the agent designs with them — all
/// from one assembly, so they cannot drift.
/// </summary>
internal sealed class WorkflowSchemaGenerator
{
    /// <summary>Format version of the generated schema document (distinct from contract versions).</summary>
    internal const string SchemaFormatVersion = "1.0";

    private readonly XmlDocReader _docs;
    private readonly IExecutableNodeTypeCatalog _executable;
    private readonly IContractTypeSet _contractTypes;

    internal WorkflowSchemaGenerator(XmlDocReader docs, IExecutableNodeTypeCatalog executable, IContractTypeSet contractTypes)
    {
        _docs = docs;
        _executable = executable;
        _contractTypes = contractTypes;
    }

    /// <summary>Reflects over the Abstractions assembly and produces the complete schema document.</summary>
    internal WorkflowSchema Generate()
    {
        var complexTypes = new HashSet<Type>();
        var nodeTypes = BuildNodeTypes(complexTypes);
        var edge = new EdgeDescriptor { Fields = BuildFields(typeof(WorkflowEdge), complexTypes) };

        return new WorkflowSchema
        {
            SchemaVersion = SchemaFormatVersion,
            NodeTypes = nodeTypes,
            Objects = BuildObjects(complexTypes),
            Edge = edge,
            Enums = BuildEnums(),
            Contracts = BuildContracts(_contractTypes),
        };
    }

    /// <summary>
    /// The valid data-contract type names (S9.72): the concrete <see cref="IVersionedContract"/>
    /// implementers in the Abstractions assembly. These are the only permitted values for a node's
    /// input/output contract and a data edge's contract type — the agent must pick from this list
    /// and match edges to consumers.
    /// </summary>
    internal static IReadOnlyList<string> BuildContracts(IContractTypeSet contractTypes) =>
        // S13.12c (E16 option 2): the set arrives from the composition root — the engine no
        // longer anchors on a workload assembly (ADR-E3a).
        contractTypes.Names;

    /// <summary>Builds a descriptor per <see cref="WorkflowNode"/> subtype, from its polymorphic attributes.</summary>
    internal IReadOnlyList<NodeTypeDescriptor> BuildNodeTypes(ISet<Type> complexTypes) =>
        typeof(WorkflowNode).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attr => BuildNodeType(attr, complexTypes))
            .ToList();

    /// <summary>Builds one node-type descriptor: discriminator, description, deprecation flag, fields.</summary>
    internal NodeTypeDescriptor BuildNodeType(JsonDerivedTypeAttribute attr, ISet<Type> complexTypes) => new()
    {
        NodeType = (string)attr.TypeDiscriminator!,
        Description = _docs.TypeSummary(attr.DerivedType),
        Deprecated = attr.DerivedType.GetCustomAttribute<ObsoleteAttribute>() is not null,
        // S13.7h: the agent is told which node types the runtime will actually run.
        Executable = _executable.IsExecutable(NodeType.FromName((string)attr.TypeDiscriminator!)),
        Fields = BuildFields(attr.DerivedType, complexTypes),
    };

    /// <summary>Builds field descriptors for a type's wire properties; records complex field types for expansion.</summary>
    internal IReadOnlyList<FieldDescriptor> BuildFields(Type type, ISet<Type> complexTypes)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                        && p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
            .OrderBy(p => p.GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? int.MaxValue)
            .ToList();

        foreach (var p in properties)
        {
            if (SchemaTypeMapper.TryGetComplexType(p.PropertyType, out var complex)) complexTypes.Add(complex);
        }

        return properties.Select(BuildField).ToList();
    }

    /// <summary>Test seam: builds fields without collecting complex types.</summary>
    internal IReadOnlyList<FieldDescriptor> BuildFields(Type type) => BuildFields(type, new HashSet<Type>());

    /// <summary>Expands the referenced complex contracts into object descriptors (closure over nested objects).</summary>
    internal IReadOnlyList<ObjectDescriptor> BuildObjects(ISet<Type> complexTypes)
    {
        var built = new Dictionary<Type, ObjectDescriptor>();
        var queue = new Queue<Type>(complexTypes);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (built.ContainsKey(type)) continue;

            var nested = new HashSet<Type>();
            built[type] = new ObjectDescriptor { Name = type.Name, Fields = BuildFields(type, nested) };
            foreach (var t in nested)
            {
                if (!built.ContainsKey(t)) queue.Enqueue(t);
            }
        }

        return built.Values.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Builds one field descriptor: wire name, type token, required flag, description.</summary>
    internal FieldDescriptor BuildField(PropertyInfo property) => new()
    {
        Name = property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name,
        Type = SchemaTypeMapper.MapToken(property.PropertyType),
        Required = property.GetCustomAttribute<RequiredMemberAttribute>() is not null,
        Description = _docs.PropertySummary(property.DeclaringType!, property.Name),
    };

    /// <summary>Builds descriptors for the domain smart enums referenced by field type tokens.</summary>
    internal static IReadOnlyList<EnumDescriptor> BuildEnums() =>
        new[] { typeof(EdgeKind), typeof(GateKind), typeof(ExecutionMode), typeof(NodeType), typeof(ComparisonOp), typeof(LogicalOp) }
            .Select(BuildEnum)
            .ToList();

    /// <summary>Builds one enum descriptor from a smart-enum type's value list.</summary>
    internal static EnumDescriptor BuildEnum(Type enumType) => new()
    {
        Name = enumType.Name,
        Values = SmartEnumValues(enumType),
    };

    /// <summary>Reads a smart enum's canonical wire values (its declared values, in order).</summary>
    internal static IReadOnlyList<string> SmartEnumValues(Type enumType)
    {
        // List is a static member on the SmartEnum<TEnum> base — FlattenHierarchy is required
        // for reflection to surface inherited statics.
        var listProperty = enumType.GetProperty(
            "List", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!;
        var values = (IEnumerable)listProperty.GetValue(null)!;
        return values.Cast<object>().Select(v => v.ToString()!).ToList();
    }
}
