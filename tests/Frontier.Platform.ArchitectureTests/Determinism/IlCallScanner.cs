using System.Reflection;
using System.Reflection.Emit;

namespace Frontier.Platform.ArchitectureTests.Determinism;

/// <summary>
/// Reads the method bodies a determinism guard needs to see: every method a given method calls,
/// resolved from its CIL <c>call</c>/<c>callvirt</c>/<c>newobj</c>/<c>ldftn</c> tokens.
/// <para>
/// Source text cannot answer this question — the orchestrator body compiles into a generated
/// async state machine, and the walk it delegates to lives in another type — so the guard reads
/// what the runtime will actually execute.
/// </para>
/// </summary>
internal static class IlCallScanner
{
    private const int TokenWidth = 4;

    /// <summary>Every method referenced by <paramref name="method"/>'s body, best-effort per token.</summary>
    internal static IReadOnlyList<MethodBase> CalledMethods(MethodBase method)
    {
        var body = SafeBody(method);
        if (body is null)
        {
            return [];
        }

        var il = body.GetILAsByteArray();
        return il is null ? [] : ResolveAll(method, il);
    }

    internal static MethodBody? SafeBody(MethodBase method)
    {
        try
        {
            return method.GetMethodBody();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            return null;
        }
    }

    internal static List<MethodBase> ResolveAll(MethodBase owner, byte[] il)
    {
        var called = new List<MethodBase>();
        foreach (var token in CallTokens(il))
        {
            var resolved = Resolve(owner, token);
            if (resolved is not null)
            {
                called.Add(resolved);
            }
        }

        return called;
    }

    /// <summary>Metadata tokens of every call-shaped instruction, walking the stream instruction by instruction.</summary>
    internal static IEnumerable<int> CallTokens(byte[] il)
    {
        var offset = 0;
        while (offset < il.Length)
        {
            var opCode = ReadOpCode(il, ref offset);
            var width = OperandWidth(opCode, il, offset);
            if (IsCall(opCode) && offset + TokenWidth <= il.Length)
            {
                yield return BitConverter.ToInt32(il, offset);
            }

            if (width < 0)
            {
                yield break;
            }

            offset += width;
        }
    }

    internal static short ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        if (first != 0xFE || offset >= il.Length)
        {
            return first;
        }

        return (short)((first << 8) | il[offset++]);
    }

    /// <summary>Operand width, resolving <c>switch</c>'s self-describing length and refusing unknown opcodes.</summary>
    internal static int OperandWidth(short opCode, byte[] il, int offset)
    {
        if (!IlOperandSizes.ByOpCodeValue.TryGetValue(opCode, out var width))
        {
            return -1;
        }

        if (width != IlOperandSizes.VariableSwitchOperand)
        {
            return width;
        }

        return offset + TokenWidth > il.Length ? -1 : TokenWidth + (TokenWidth * BitConverter.ToInt32(il, offset));
    }

    internal static bool IsCall(short opCode) =>
        opCode == OpCodes.Call.Value
        || opCode == OpCodes.Callvirt.Value
        || opCode == OpCodes.Newobj.Value
        || opCode == OpCodes.Ldftn.Value
        || opCode == OpCodes.Ldvirtftn.Value;

    /// <summary>
    /// Resolves one token against the owner's generic context. Tokens that will not resolve — a
    /// call site the reflection APIs cannot reconstruct — are dropped rather than failing the
    /// scan, so an unreadable instruction weakens coverage instead of breaking the build.
    /// </summary>
    internal static MethodBase? Resolve(MethodBase owner, int token)
    {
        try
        {
            return owner.Module.ResolveMethod(token, GenericArgumentsOf(owner.DeclaringType), MethodGenericArgumentsOf(owner));
        }
        catch (Exception e) when (e is ArgumentException or MissingMemberException or BadImageFormatException or NotSupportedException)
        {
            return null;
        }
    }

    internal static Type[]? GenericArgumentsOf(Type? type) =>
        type is { IsGenericType: true } ? type.GetGenericArguments() : null;

    internal static Type[]? MethodGenericArgumentsOf(MethodBase method) =>
        method.IsGenericMethodDefinition || method.IsGenericMethod ? method.GetGenericArguments() : null;
}
