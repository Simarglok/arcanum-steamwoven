using Arcanum.Executable;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Arcanum.Executable.Tests;

/// <summary>
/// Synthetic machine code for the interpreter tests: hand-assembled bytes (Intel SDM encodings) at <see cref="Code"/>, a
/// read-write <see cref="Data"/> region, and a trailing <c>ret</c> so a snippet runs as one call. No original bytes.
/// </summary>
internal static class Cpu
{
    public const uint Code = 0x0040_1000;
    public const uint CodeSize = 0x4000;
    public const uint Data = 0x0050_0000;
    public const uint DataSize = 0x4000;

    public static X86Machine Machine(string hex, MachineOptions? options = null)
    {
        var machine = new X86Machine(options);
        machine.Memory.Map(Code, CodeSize, MemoryAccess.ReadExecute, "code");
        machine.Memory.Map(Data, DataSize, MemoryAccess.ReadWrite, "data");
        machine.Memory.Load(Code, [.. Bytes(hex), 0xC3]);
        return machine;
    }

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

    /// <summary>Runs the snippet and requires that it returned.</summary>
    public static X86Machine Run(string hex, Action<X86Machine>? setup = null)
    {
        var machine = Machine(hex);
        setup?.Invoke(machine);
        var result = machine.Call(Code, [], 100_000);
        Assert.IsTrue(result.Completed, $"{hex}: {result.Fault}");
        return machine;
    }

    /// <summary>Runs the snippet and returns its fault.</summary>
    public static MachineFault Fault(string hex, Action<X86Machine>? setup = null, long steps = 100_000)
    {
        var machine = Machine(hex);
        setup?.Invoke(machine);
        var result = machine.Call(Code, [], steps);
        Assert.IsFalse(result.Completed, $"{hex} returned");
        return result.Fault!;
    }

    /// <summary>
    /// Checks the named flags only: "C1 Z0" requires CF set and ZF clear. Letters: C carry, P parity, A adjust, Z zero,
    /// S sign, O overflow, D direction.
    /// </summary>
    public static void Flags(X86Machine machine, string expected)
    {
        foreach (var item in expected.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var actual = item[0] switch
            {
                'C' => machine.Carry,
                'P' => machine.Parity,
                'A' => machine.Adjust,
                'Z' => machine.Zero,
                'S' => machine.Sign,
                'O' => machine.Overflow,
                'D' => machine.Direction,
                _ => throw new ArgumentException(item),
            };
            Assert.AreEqual(item[1] == '1', actual, $"flag {item[0]} (expected {expected}, EFLAGS 0x{machine.Eflags:X})");
        }
    }

    /// <summary>Sets the six arithmetic flags from the same notation.</summary>
    public static void SetFlags(X86Machine machine, string flags)
    {
        foreach (var item in flags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = item[1] == '1';
            switch (item[0])
            {
                case 'C': machine.Carry = value; break;
                case 'P': machine.Parity = value; break;
                case 'A': machine.Adjust = value; break;
                case 'Z': machine.Zero = value; break;
                case 'S': machine.Sign = value; break;
                case 'O': machine.Overflow = value; break;
                case 'D': machine.Direction = value; break;
                default: throw new ArgumentException(item);
            }
        }
    }

    public static string Hex(uint value) => value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Little-endian bytes of a 32-bit value as hex, for building snippets.</summary>
    public static string Le(uint value) => Convert.ToHexString(BitConverter.GetBytes(value));
}
