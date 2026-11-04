using System.Numerics;

namespace Arcanum.Executable;

/// <summary>
/// The flag rules of the integer subset (Intel SDM vol. 2). Where the manual leaves a flag undefined the interpreter keeps
/// a fixed choice, written next to each rule; compiled code does not read an undefined flag, and the tests check only
/// defined flags.
/// </summary>
public sealed partial class X86Machine
{
    private static uint Mask(int size) => size == 4 ? 0xFFFF_FFFFu : size == 2 ? 0xFFFFu : 0xFFu;

    private static uint SignBit(int size) => size == 4 ? 0x8000_0000u : size == 2 ? 0x8000u : 0x80u;

    private static long SignExtend(uint value, int size) => size == 4 ? (int)value : size == 2 ? (short)value : (sbyte)value;

    private void SetResultFlags(uint result, int size)
    {
        zf = (result & Mask(size)) == 0;
        sf = (result & SignBit(size)) != 0;
        pf = (BitOperations.PopCount(result & 0xFF) & 1) == 0;
    }

    /// <summary>add, or, adc, sbb, and, sub, xor, cmp (the ModRM reg order); returns the result, which cmp does not store.</summary>
    private uint Alu(int operation, uint a, uint b, int size)
    {
        var mask = Mask(size);
        var sign = SignBit(size);
        a &= mask;
        b &= mask;
        uint result;
        switch (operation)
        {
            case 0: // add
            case 2: // adc
                {
                    var sum = (ulong)a + b + (operation == 2 && cf ? 1u : 0u);
                    result = (uint)sum & mask;
                    cf = sum > mask;
                    of = ((a ^ result) & (b ^ result) & sign) != 0;
                    af = ((a ^ b ^ result) & 0x10) != 0;
                }
                break;
            case 3: // sbb
            case 5: // sub
            case 7: // cmp
                {
                    var borrow = operation == 3 && cf ? 1u : 0u;
                    result = (a - b - borrow) & mask;
                    cf = a < (ulong)b + borrow;
                    of = ((a ^ b) & (a ^ result) & sign) != 0;
                    af = ((a ^ b ^ result) & 0x10) != 0;
                }
                break;
            default: // or, and, xor: CF and OF cleared; AF undefined, cleared here
                result = operation == 1 ? a | b : operation == 4 ? a & b : a ^ b;
                cf = of = af = false;
                break;
        }
        SetResultFlags(result, size);
        return result;
    }

    /// <summary>inc keeps CF.</summary>
    private uint Inc(uint a, int size)
    {
        var result = (a + 1) & Mask(size);
        of = result == SignBit(size);
        af = (result & 0xF) == 0;
        SetResultFlags(result, size);
        return result;
    }

    /// <summary>dec keeps CF.</summary>
    private uint Dec(uint a, int size)
    {
        a &= Mask(size);
        var result = (a - 1) & Mask(size);
        of = a == SignBit(size);
        af = (a & 0xF) == 0;
        SetResultFlags(result, size);
        return result;
    }

    /// <summary>
    /// rol, ror, rcl, rcr, shl, shr, sal (= shl), sar by a count already masked to 5 bits and not 0. OF is defined only for a
    /// count of 1; for larger counts it is computed by the same rule. AF is undefined after a shift and kept. Rotates set
    /// only CF and OF. A shift of an 8- or 16-bit operand by its width or more leaves 0 (or the sign), with CF the last bit
    /// out (0 past the width).
    /// </summary>
    private uint Shift(int operation, uint a, uint count, int size)
    {
        var bits = size * 8;
        var mask = Mask(size);
        var sign = SignBit(size);
        a &= mask;
        uint result;
        switch (operation)
        {
            case 0: // rol
                {
                    var n = (int)(count % (uint)bits);
                    result = n == 0 ? a : ((a << n) | (a >> (bits - n))) & mask;
                    cf = (result & 1) != 0;
                    of = ((result & sign) != 0) ^ cf;
                    return result;
                }
            case 1: // ror
                {
                    var n = (int)(count % (uint)bits);
                    result = n == 0 ? a : ((a >> n) | (a << (bits - n))) & mask;
                    cf = (result & sign) != 0;
                    of = cf ^ ((result & (sign >> 1)) != 0);
                    return result;
                }
            case 2: // rcl: through CF, over bits + 1 positions
                {
                    var n = size == 4 ? (int)count : (int)(count % (uint)(bits + 1));
                    result = a;
                    for (var i = 0; i < n; i++)
                    {
                        var carryOut = (result & sign) != 0;
                        result = ((result << 1) | (cf ? 1u : 0u)) & mask;
                        cf = carryOut;
                    }
                    of = ((result & sign) != 0) ^ cf;
                    return result;
                }
            case 3: // rcr
                {
                    var n = size == 4 ? (int)count : (int)(count % (uint)(bits + 1));
                    of = ((a & sign) != 0) ^ cf;
                    result = a;
                    for (var i = 0; i < n; i++)
                    {
                        var carryOut = (result & 1) != 0;
                        result = (result >> 1) | (cf ? sign : 0u);
                        cf = carryOut;
                    }
                    return result;
                }
            case 4: // shl
            case 6: // sal
                if (count < bits)
                {
                    result = (uint)(((ulong)a << (int)count) & mask);
                    cf = ((a >> (bits - (int)count)) & 1) != 0;
                }
                else
                {
                    result = 0;
                    cf = count == bits && (a & 1) != 0;
                }
                of = ((result & sign) != 0) ^ cf;
                break;
            case 5: // shr
                of = (a & sign) != 0;
                if (count < bits)
                {
                    result = a >> (int)count;
                    cf = ((a >> ((int)count - 1)) & 1) != 0;
                }
                else
                {
                    result = 0;
                    cf = count == bits && (a & sign) != 0;
                }
                break;
            default: // sar
                {
                    var signed = SignExtend(a, size);
                    result = (uint)(signed >> (int)count) & mask;
                    cf = ((signed >> ((int)count - 1)) & 1) != 0;
                    of = false;
                }
                break;
        }
        SetResultFlags(result, size);
        return result;
    }

    /// <summary>shld and shrd of 32-bit operands by 1..31; OF as for a count of 1 (sign changed), AF kept.</summary>
    private uint DoubleShift(bool left, uint destination, uint source, int count)
    {
        uint result;
        if (left)
        {
            result = (destination << count) | (source >> (32 - count));
            cf = ((destination >> (32 - count)) & 1) != 0;
        }
        else
        {
            result = (destination >> count) | (source << (32 - count));
            cf = ((destination >> (count - 1)) & 1) != 0;
        }
        of = ((result ^ destination) & 0x8000_0000) != 0;
        SetResultFlags(result, 4);
        return result;
    }

    /// <summary>The two- and three-operand imul: the truncated product; CF = OF = the product did not fit. SF, ZF, PF, AF kept.</summary>
    private uint MultiplySigned(uint a, uint b, int size)
    {
        var product = SignExtend(a, size) * SignExtend(b, size);
        var result = (uint)product & Mask(size);
        cf = of = SignExtend(result, size) != product;
        return result;
    }

    /// <summary>One-operand mul and imul into AX, DX:AX or EDX:EAX; CF = OF = the high half is significant. SF, ZF, PF, AF kept.</summary>
    private void MultiplyAccumulator(uint source, int size, bool signed)
    {
        switch (size)
        {
            case 1:
                {
                    var product = signed ? (sbyte)r[EaxIndex] * (sbyte)source : (int)((r[EaxIndex] & 0xFF) * (source & 0xFF));
                    SetReg(EaxIndex, 2, (uint)product);
                    cf = of = signed ? product != (sbyte)product : (product & 0xFF00) != 0;
                }
                break;
            case 2:
                {
                    var product = signed ? (short)r[EaxIndex] * (short)source : (long)((r[EaxIndex] & 0xFFFF) * (source & 0xFFFF));
                    SetReg(EaxIndex, 2, (uint)product);
                    SetReg(EdxIndex, 2, (uint)(product >> 16));
                    cf = of = signed ? product != (short)product : (product >> 16) != 0;
                }
                break;
            default:
                if (signed)
                {
                    var product = (long)(int)r[EaxIndex] * (int)source;
                    r[EaxIndex] = (uint)product;
                    r[EdxIndex] = (uint)((ulong)product >> 32);
                    cf = of = product != (int)product;
                }
                else
                {
                    var product = (ulong)r[EaxIndex] * source;
                    r[EaxIndex] = (uint)product;
                    r[EdxIndex] = (uint)(product >> 32);
                    cf = of = r[EdxIndex] != 0;
                }
                break;
        }
    }

    /// <summary>div and idiv of AX, DX:AX or EDX:EAX; a zero divisor or a quotient out of range is the CPU's #DE. Flags kept (undefined).</summary>
    private void Divide(uint source, int size, bool signed)
    {
        source &= Mask(size);
        if (source == 0) throw Fault(MachineFaultKind.DivideError, "divide by zero");
        if (!signed)
        {
            ulong dividend = size switch
            {
                1 => r[EaxIndex] & 0xFFFF,
                2 => (r[EdxIndex] & 0xFFFF) << 16 | (r[EaxIndex] & 0xFFFF),
                _ => (ulong)r[EdxIndex] << 32 | r[EaxIndex],
            };
            var quotient = dividend / source;
            var remainder = dividend % source;
            if (quotient > Mask(size)) throw Fault(MachineFaultKind.DivideError, "quotient out of range");
            StoreDivision(size, (uint)quotient, (uint)remainder);
        }
        else
        {
            long dividend = size switch
            {
                1 => (short)r[EaxIndex],
                2 => (int)((r[EdxIndex] & 0xFFFF) << 16 | (r[EaxIndex] & 0xFFFF)),
                _ => (long)((ulong)r[EdxIndex] << 32 | r[EaxIndex]),
            };
            var divisor = SignExtend(source, size);
            if (dividend == long.MinValue && divisor == -1) throw Fault(MachineFaultKind.DivideError, "quotient out of range");
            var quotient = dividend / divisor;
            var remainder = dividend % divisor;
            var limit = (long)SignBit(size);
            if (quotient < -limit || quotient >= limit) throw Fault(MachineFaultKind.DivideError, "quotient out of range");
            StoreDivision(size, (uint)quotient, (uint)remainder);
        }
    }

    private void StoreDivision(int size, uint quotient, uint remainder)
    {
        switch (size)
        {
            case 1:
                SetReg(EaxIndex, 1, quotient);
                SetReg(4, 1, remainder);
                break;
            case 2:
                SetReg(EaxIndex, 2, quotient);
                SetReg(EdxIndex, 2, remainder);
                break;
            default:
                r[EaxIndex] = quotient;
                r[EdxIndex] = remainder;
                break;
        }
    }

    /// <summary>The 16 conditions of jcc, setcc and cmovcc in encoding order.</summary>
    private bool Condition(int code)
    {
        var value = (code >> 1) switch
        {
            0 => of,
            1 => cf,
            2 => zf,
            3 => cf || zf,
            4 => sf,
            5 => pf,
            6 => sf != of,
            _ => zf || sf != of,
        };
        return (code & 1) != 0 ? !value : value;
    }
}
