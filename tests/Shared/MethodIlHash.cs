using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Tests.Shared;

/// <summary>
/// SHA-256 of a method's IL with every metadata token replaced by the name of what it refers to (ARC-DEV-008 receipts),
/// so the hash changes when the method's code changes and not when unrelated members shift the assembly's token numbers.
/// Locals, exception clauses and the build configuration are part of the text; Debug and Release builds hash differently.
/// Linked into the test projects that write or check oracle receipts.
/// </summary>
internal static class MethodIlHash
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static string Compute(MethodInfo method) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(method)))).ToLowerInvariant();

    public static string Configuration(MethodInfo method) =>
        method.Module.Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "";

    public static string Name(MethodInfo method) => $"{method.DeclaringType!.FullName}.{method.Name}";

    internal static string Text(MethodInfo method)
    {
        var body = method.GetMethodBody() ?? throw new ArgumentException($"{Name(method)} has no IL body");
        var il = body.GetILAsByteArray() ?? [];
        var module = method.Module;
        var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var text = new StringBuilder();
        text.Append(Name(method)).Append('(').AppendJoin(',', method.GetParameters().Select(p => p.ParameterType.FullName)).Append(")\n");
        text.Append("init ").Append(body.InitLocals).Append('\n');
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
            if (!OpCodesByValue.TryGetValue(value, out var op)) throw new InvalidDataException($"unknown IL opcode {value:X} at {start} in {Name(method)}");
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
                    text.Append(' ').Append(member is Type type ? type.FullName : $"{member.DeclaringType?.FullName}::{member}");
                    at += 4;
                    break;
                default:
                    throw new InvalidDataException($"unhandled operand type {op.OperandType} in {Name(method)}");
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}
