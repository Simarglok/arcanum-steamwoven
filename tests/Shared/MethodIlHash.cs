using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Tests.Shared;

/// <summary>
/// SHA-256 of a method's IL and of the IL of every method and constructor of the same assembly it reaches (callees,
/// property accessors, lambdas, local functions, state machines), each with every metadata token replaced by the name of
/// what it refers to (ARC-DEV-008 receipts). The hash changes when any of that code changes and not when unrelated
/// members shift the assembly's token numbers; compiler-generated names (which carry ordinals) are replaced by their place
/// in the walk. Locals and exception clauses are part of the text; Debug and Release builds, and a different runtime
/// (its type names appear in the text), hash differently. Linked into the test projects that write or check receipts.
/// </summary>
internal static partial class MethodIlHash
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static string Compute(MethodInfo method) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(method)))).ToLowerInvariant();

    /// <summary>The method and every same-assembly method it reaches, in walk order (the method first). A compiler-generated
    /// type of the assembly that appears (a closure, a state machine) brings all its methods into the walk.</summary>
    internal static string Text(MethodInfo method)
    {
        var order = new List<MethodBase> { method };
        int Reach(MethodBase reached)
        {
            var index = order.IndexOf(reached);
            if (index < 0) { index = order.Count; order.Add(reached); }
            return index;
        }
        void ReachType(Type? type)
        {
            if (type is null || type.Module != method.Module || !Generated(type)) return;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).OrderBy(m => m.MetadataToken)) Reach(member);
        }
        var text = new StringBuilder();
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i].GetMethodBody() is null) continue; // abstract, extern or runtime-provided
            text.Append("== ").Append(i).Append('\n');
            Body(order[i], text, reached => reached.Module == method.Module ? Reach(reached) : null, ReachType);
        }
        // Compiler-generated names carry ordinals of their containing method (<M>b__12_0, <>c__DisplayClass3_1); drop them.
        return GeneratedOrdinal().Replace(text.ToString(), "$1#");
    }

    private static bool Generated(MemberInfo member) => member.Name.Contains('<') || (member.DeclaringType is { } d && Generated(d));

    [System.Text.RegularExpressions.GeneratedRegex(@"(__[A-Za-z]*)\d+(_\d+)*")]
    private static partial System.Text.RegularExpressions.Regex GeneratedOrdinal();

    public static string Configuration(MethodInfo method) =>
        method.Module.Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "";

    public static string Name(MethodInfo method) => $"{method.DeclaringType!.FullName}.{method.Name}";

    private static void Body(MethodBase method, StringBuilder text, Func<MethodBase, int?> reach, Action<Type?> reachType)
    {
        var body = method.GetMethodBody()!;
        reachType(method.DeclaringType);
        var il = body.GetILAsByteArray() ?? [];
        var module = method.Module;
        var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;
        text.Append(method.DeclaringType!.FullName).Append('.').Append(method.Name).Append('(').AppendJoin(',', method.GetParameters().Select(p => p.ParameterType.FullName)).Append(")\n");
        text.Append("init ").Append(body.InitLocals).Append('\n');
        foreach (var local in body.LocalVariables) reachType(local.LocalType);
        foreach (var local in body.LocalVariables) text.Append("local ").Append(local.LocalIndex).Append(' ').Append(local.LocalType.FullName).Append('\n');
        foreach (var clause in body.ExceptionHandlingClauses)
            text.Append("clause ").Append(clause.Flags).Append(' ').Append(clause.TryOffset).Append(' ').Append(clause.TryLength).Append(' ')
                .Append(clause.HandlerOffset).Append(' ').Append(clause.HandlerLength).Append(' ')
                .Append(clause.Flags == ExceptionHandlingClauseOptions.Clause ? clause.CatchType?.FullName : clause.FilterOffset.ToString()).Append('\n');
        for (var at = 0; at < il.Length;)
        {
            var start = at;
            short value = il[at++];
            if (value == 0xFE) value = unchecked((short)(0xFE00 | il[at++]));
            if (!OpCodesByValue.TryGetValue(value, out var op)) throw new InvalidDataException($"unknown IL opcode {value:X} at {start} in {method.Name}");
            text.Append(start).Append(' ').Append(op.Name);
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    text.Append(' ').Append(il[at]);
                    at += 1;
                    break;
                case OperandType.InlineVar:
                    text.Append(' ').Append(BitConverter.ToUInt16(il, at));
                    at += 2;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                    text.Append(' ').Append(BitConverter.ToInt32(il, at));
                    at += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    text.Append(' ').Append(BitConverter.ToInt64(il, at));
                    at += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, at);
                    at += 4;
                    for (var i = 0; i < count; i++, at += 4) text.Append(' ').Append(BitConverter.ToInt32(il, at));
                    break;
                case OperandType.InlineString:
                    text.Append(" \"").Append(module.ResolveString(BitConverter.ToInt32(il, at))).Append('"');
                    at += 4;
                    break;
                case OperandType.InlineSig:
                    text.Append(" sig ").Append(Convert.ToHexString(module.ResolveSignature(BitConverter.ToInt32(il, at))));
                    at += 4;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var member = module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!;
                    reachType(member as Type ?? member.DeclaringType);
                    if (member is MethodBase target && reach(target) is { } reached) text.Append(" #").Append(reached);
                    text.Append(' ').Append(member is Type type ? type.FullName : $"{member.DeclaringType?.FullName}::{member}");
                    at += 4;
                    break;
                default:
                    throw new InvalidDataException($"unhandled operand type {op.OperandType} in {method.Name}");
            }
            text.Append('\n');
        }
    }
}
