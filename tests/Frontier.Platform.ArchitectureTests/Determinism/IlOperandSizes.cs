using System.Reflection;
using System.Reflection.Emit;

namespace Frontier.Platform.ArchitectureTests.Determinism;

/// <summary>
/// Operand widths for CIL opcodes, built by reflecting over <see cref="OpCodes"/> rather than
/// hand-transcribed. A hand-written table is the kind of thing that is correct on the day it is
/// written and silently wrong afterwards; the runtime already ships the authority.
/// </summary>
internal static class IlOperandSizes
{
    /// <summary>Opcode value (two-byte opcodes keep their <c>0xFE</c> prefix) → operand width in bytes.</summary>
    internal static readonly IReadOnlyDictionary<short, int> ByOpCodeValue = Build();

    /// <summary>Marker width for <c>switch</c>, whose operand length depends on its own first four bytes.</summary>
    internal const int VariableSwitchOperand = -1;

    internal static Dictionary<short, int> Build()
    {
        var map = new Dictionary<short, int>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
            {
                map[opCode.Value] = WidthOf(opCode.OperandType);
            }
        }

        return map;
    }

    internal static int WidthOf(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => VariableSwitchOperand,
        _ => 4,
    };
}
